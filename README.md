# Finances personals

Aplicació local per registrar ingressos i despeses amb múltiples tags,
agrupats per dimensions com proveïdor, tipus i subtipus.

## Executar

Des de PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\run-app.ps1
```

Obre `http://localhost:5292` al navegador.

## Compilar

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build-app.ps1
```

## Estructura

- `client`: interfície Angular.
- `server`: API ASP.NET Core i base de dades SQLite.
- `server/data/finances.db`: dades locals, creada en la primera execució.

La primera versió permet:

- Registrar ingressos i despeses.
- Assignar diversos tags segons les regles de cada grup.
- Crear tags nous.
- Consultar totals per setmana, mes o any.
- Veure totals per tag sense alterar el total general.
- Eliminar moviments.

## Importar un Excel

Amb l'aplicació iniciada:

```powershell
.\import-xlsx.ps1 -FilePath "C:\ruta\al\fitxer.xlsx"
```

L'importador llegeix els fulls amb nom `AAAA-1` o `AAAA-2`, interpreta les
columnes C i D com Tipus i Subtipus, crea els tags que falten i evita tornar
a importar moviments duplicats.
