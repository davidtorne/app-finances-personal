param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,
    [string]$ApiUrl = "http://localhost:5292"
)

$ErrorActionPreference = "Stop"
$resolvedFile = (Resolve-Path -LiteralPath $FilePath).Path
$tempRoot = [IO.Path]::GetTempPath()
$tempDirectory = Join-Path $tempRoot ("personal-finances-import-" + [guid]::NewGuid())

function Read-Utf8Xml([string]$Path) {
    return [xml][IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
}

function Get-CellValue($Cell, $Namespace, $SharedStrings) {
    $valueNode = $Cell.SelectSingleNode("./x:v", $Namespace)
    if ($null -eq $valueNode) {
        return ""
    }

    if ($Cell.t -eq "s") {
        return $SharedStrings[[int]$valueNode.InnerText]
    }

    return $valueNode.InnerText
}

function Get-NormalizedKey(
    [string]$Type,
    [decimal]$Amount,
    [string]$Date,
    [string]$Description
) {
    $normalizedDescription = $Description.Trim().ToLowerInvariant()
    $normalizedAmount = $Amount.ToString("0.00", [Globalization.CultureInfo]::InvariantCulture)
    return "$Type|$normalizedAmount|$Date|$normalizedDescription"
}

function Invoke-JsonPost([string]$Url, $Body) {
    $json = $Body | ConvertTo-Json -Depth 6
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    return (
        Invoke-RestMethod $Url `
            -Method Post `
            -ContentType "application/json; charset=utf-8" `
            -Body $bytes
    )
}

try {
    Invoke-RestMethod "$ApiUrl/api/health" | Out-Null

    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($resolvedFile, $tempDirectory)

    $excelDirectory = Join-Path $tempDirectory "xl"
    $sharedXml = Read-Utf8Xml (Join-Path $excelDirectory "sharedStrings.xml")
    $sharedNamespace = New-Object Xml.XmlNamespaceManager($sharedXml.NameTable)
    $sharedNamespace.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")

    $sharedStrings = @()
    foreach ($item in $sharedXml.SelectNodes("//x:si", $sharedNamespace)) {
        $text = $item.SelectNodes(".//x:t", $sharedNamespace) |
            ForEach-Object { $_.InnerText }
        $sharedStrings += ($text -join "")
    }

    $workbookXml = Read-Utf8Xml (Join-Path $excelDirectory "workbook.xml")
    $workbookNamespace = New-Object Xml.XmlNamespaceManager($workbookXml.NameTable)
    $workbookNamespace.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
    $workbookNamespace.AddNamespace("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")

    $relationshipsXml = Read-Utf8Xml (Join-Path $excelDirectory "_rels\workbook.xml.rels")
    $relationshipNamespace = New-Object Xml.XmlNamespaceManager($relationshipsXml.NameTable)
    $relationshipNamespace.AddNamespace("r", "http://schemas.openxmlformats.org/package/2006/relationships")

    $worksheetPaths = @{}
    foreach ($relationship in $relationshipsXml.SelectNodes("//r:Relationship", $relationshipNamespace)) {
        $worksheetPaths[$relationship.Id] = $relationship.Target
    }

    $records = @()
    foreach ($sheet in $workbookXml.SelectNodes("//x:sheets/x:sheet", $workbookNamespace)) {
        if ($sheet.name -notmatch "^\d{4}-[12]$") {
            continue
        }

        $relationshipId = $sheet.GetAttribute(
            "id",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
        )
        $worksheetPath = Join-Path $excelDirectory $worksheetPaths[$relationshipId]
        $worksheetXml = Read-Utf8Xml $worksheetPath
        $worksheetNamespace = New-Object Xml.XmlNamespaceManager($worksheetXml.NameTable)
        $worksheetNamespace.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")

        foreach ($row in $worksheetXml.SelectNodes("//x:sheetData/x:row", $worksheetNamespace)) {
            if ([int]$row.r -lt 2) {
                continue
            }

            $cells = @{}
            foreach ($cell in $row.SelectNodes("./x:c", $worksheetNamespace)) {
                $column = ([regex]::Match($cell.r, "^[A-Z]+")).Value
                if ($column -in @("A", "B", "C", "D", "E")) {
                    $cells[$column] = Get-CellValue $cell $worksheetNamespace $sharedStrings
                }
            }

            $amountValue = 0.0
            $dateSerial = 0.0
            $hasAmount = [double]::TryParse(
                [string]$cells["E"],
                [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$amountValue
            )
            $hasDate = [double]::TryParse(
                [string]$cells["A"],
                [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$dateSerial
            )

            if (-not $hasAmount -or -not $hasDate -or $amountValue -eq 0) {
                continue
            }

            $description = ([string]$cells["B"]).Trim()
            $typeTag = ([string]$cells["C"]).Trim()
            $subtypeTag = ([string]$cells["D"]).Trim()
            if (-not $description -or -not $typeTag -or -not $subtypeTag) {
                continue
            }

            $records += [pscustomobject]@{
                Date = [DateTime]::FromOADate($dateSerial).ToString("yyyy-MM-dd")
                Description = $description
                TypeTag = $typeTag
                SubtypeTag = $subtypeTag
                TransactionType = if ($amountValue -gt 0) { "income" } else { "expense" }
                Amount = [decimal][math]::Abs($amountValue)
            }
        }
    }

    $tagGroupResponse = Invoke-RestMethod "$ApiUrl/api/tag-groups"
    $tagGroups = @($tagGroupResponse | ForEach-Object { $_ })
    $typeGroup = $tagGroups | Where-Object { $_.name -eq "Tipus" } | Select-Object -First 1
    $subtypeGroup = $tagGroups | Where-Object { $_.name -eq "Subtipus" } | Select-Object -First 1

    if ($null -eq $typeGroup -or $null -eq $subtypeGroup) {
        throw "No s'han trobat els grups de tags Tipus i Subtipus."
    }

    $typeTags = @{}
    foreach ($tag in $typeGroup.tags) {
        $typeTags[$tag.name] = [int]$tag.id
    }

    $subtypeTags = @{}
    foreach ($tag in $subtypeGroup.tags) {
        $subtypeTags[$tag.name] = [int]$tag.id
    }

    $typeColors = @("#2563eb", "#7c3aed", "#0891b2", "#059669", "#dc2626")
    $subtypeColors = @("#16a34a", "#ea580c", "#9333ea", "#0284c7", "#ca8a04")
    $createdTags = 0

    foreach ($name in ($records.TypeTag | Sort-Object -Unique)) {
        if (-not $typeTags.ContainsKey($name)) {
            $color = $typeColors[$createdTags % $typeColors.Count]
            $tagId = Invoke-JsonPost "$ApiUrl/api/tags" @{
                tagGroupId = [int]$typeGroup.id
                name = $name
                color = $color
                parentTagId = $null
            }
            $typeTags[$name] = [int]$tagId
            $createdTags++
        }
    }

    foreach ($name in ($records.SubtypeTag | Sort-Object -Unique)) {
        if (-not $subtypeTags.ContainsKey($name)) {
            $color = $subtypeColors[$createdTags % $subtypeColors.Count]
            $tagId = Invoke-JsonPost "$ApiUrl/api/tags" @{
                tagGroupId = [int]$subtypeGroup.id
                name = $name
                color = $color
                parentTagId = $null
            }
            $subtypeTags[$name] = [int]$tagId
            $createdTags++
        }
    }

    $existingKeys = New-Object "System.Collections.Generic.HashSet[string]"
    $transactionResponse = Invoke-RestMethod "$ApiUrl/api/transactions/"
    $existingTransactions = @($transactionResponse | ForEach-Object { $_ })
    foreach ($transaction in $existingTransactions) {
        $key = Get-NormalizedKey `
            $transaction.type `
            ([decimal]$transaction.amount) `
            $transaction.date `
            $transaction.description
        [void]$existingKeys.Add($key)
    }

    $imported = 0
    $skipped = 0
    foreach ($record in $records) {
        $key = Get-NormalizedKey `
            $record.TransactionType `
            $record.Amount `
            $record.Date `
            $record.Description

        if ($existingKeys.Contains($key)) {
            $skipped++
            continue
        }

        Invoke-JsonPost "$ApiUrl/api/transactions/" @{
            type = $record.TransactionType
            amount = $record.Amount
            date = $record.Date
            description = $record.Description
            tagIds = @(
                [int]$typeTags[$record.TypeTag],
                [int]$subtypeTags[$record.SubtypeTag]
            )
        } | Out-Null

        [void]$existingKeys.Add($key)
        $imported++

        if ($imported % 50 -eq 0) {
            Write-Host "$imported moviments importats..."
        }
    }

    Write-Host ""
    Write-Host "Importació completada." -ForegroundColor Green
    Write-Host "Moviments llegits: $($records.Count)"
    Write-Host "Moviments importats: $imported"
    Write-Host "Duplicats omesos: $skipped"
    Write-Host "Tags creats: $createdTags"
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($tempDirectory)
    $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
    if (
        $resolvedTemp.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemp)
    ) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
