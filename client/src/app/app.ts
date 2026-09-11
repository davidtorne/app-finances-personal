import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  debounceTime,
  distinctUntilChanged,
  finalize,
  forkJoin,
  map,
  Observable,
  of,
  Subject,
  Subscription,
  switchMap,
} from 'rxjs';
import { FinanceApiService } from './finance-api.service';
import {
  BackupResult,
  Budget,
  BudgetItem,
  FinanceSummary,
  FinanceTransaction,
  FixedExpense,
  MonthlyFixedExpense,
  SavingsAccount,
  SavingsMovement,
  SavingsMovementType,
  TagGroup,
  TransactionTag,
  TransactionType,
  WeeklyForecast,
} from './finance.models';

interface TransactionSuggestion {
  kind: 'transaction' | 'fixed-expense';
  type: TransactionType;
  description: string;
  amount: number;
  tags: TransactionTag[];
  monthLabel?: string;
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit, OnDestroy {
  protected activeSection: 'consultation' | 'entry' | 'budgets' | 'fixed-expenses' | 'weekly-forecast' | 'savings' = 'consultation';
  protected readonly summary = signal<FinanceSummary | null>(null);
  protected readonly transactions = signal<FinanceTransaction[]>([]);
  protected readonly tagGroups = signal<TagGroup[]>([]);
  protected period: 'week' | 'month' | 'year' = 'month';
  protected anchor = this.today();
  protected readonly periodFrom = signal('');
  protected readonly periodTo = signal('');
  protected typeFilterId = 0;
  protected subtypeFilterId = 0;
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly savingTag = signal(false);
  protected readonly savingBudget = signal(false);
  protected readonly savingBudgetCopy = signal(false);
  protected readonly savingBudgetItem = signal(false);
  protected readonly backingUp = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');

  protected readonly backups = signal<BackupResult[]>([]);
  protected readonly loadingBackups = signal(false);
  protected readonly downloadingBackupFileName = signal<string | null>(null);
  protected showBackupList = false;

  protected transactionForm = {
    type: 'expense' as TransactionType,
    amount: null as number | null,
    date: this.today(),
    description: '',
  };
  protected transactionAmountText = '';

  protected selectedTagIds = new Set<number>();
  protected readonly transactionSuggestions = signal<TransactionSuggestion[]>([]);
  private readonly descriptionInput$ = new Subject<string>();
  protected newTag = {
    tagGroupId: 0,
    name: '',
    color: '#2563eb',
  };
  protected readonly budgets = signal<Budget[]>([]);
  protected selectedBudgetId = 0;
  protected budgetForm = {
    name: '',
    from: this.firstDayOfCurrentMonth(),
    to: this.lastDayOfCurrentMonth(),
  };
  protected budgetItemForm = {
    type: 'expense' as TransactionType,
    description: '',
    expectedAmount: null as number | null,
  };
  protected selectedBudgetTagIds = new Set<number>();
  protected editingBudgetItemId = 0;
  protected showBudgetCopyForm = false;
  protected budgetCopyForm = {
    name: '',
    from: '',
    to: '',
  };

  protected readonly fixedExpenses = signal<FixedExpense[]>([]);
  protected readonly savingFixedExpense = signal(false);
  protected fixedExpenseForm = {
    type: 'expense' as TransactionType,
    description: '',
    amount: null as number | null,
    month: new Date().getMonth() + 1,
  };
  protected selectedFixedExpenseTagIds = new Set<number>();
  protected editingFixedExpenseId = 0;
  protected readonly monthNames = [
    'Gener', 'Febrer', 'Març', 'Abril', 'Maig', 'Juny',
    'Juliol', 'Agost', 'Setembre', 'Octubre', 'Novembre', 'Desembre',
  ];

  protected readonly weekLabels = ['Primera setmana', 'Segona setmana', 'Tercera setmana', 'Últims de mes'];
  protected readonly weeklyForecast = signal<WeeklyForecast | null>(null);
  protected forecastAnchor = this.today();
  protected expandedWeekNumber: number | null = null;
  protected showForecastSettings = false;
  protected readonly savingFixedExpenseForecastId = signal<number | null>(null);

  protected readonly monthlyFixedExpenses = signal<MonthlyFixedExpense[]>([]);
  protected readonly savingMonthlyFixedExpense = signal(false);
  protected monthlyFixedExpenseForm = {
    description: '',
    amount: null as number | null,
    week: 1,
  };
  protected editingMonthlyFixedExpenseId = 0;

  protected readonly savingsAccounts = signal<SavingsAccount[]>([]);
  protected readonly savingsMovements = signal<SavingsMovement[]>([]);
  protected readonly savingSavingsMovement = signal(false);
  protected readonly savingSavingsAccount = signal(false);
  protected savingsMovementFilterAccountId = 0;
  protected savingsYear = new Date().getFullYear();
  protected savingsMovementForm = {
    savingsAccountId: 0,
    type: 'deposit' as SavingsMovementType,
    amount: null as number | null,
    date: this.today(),
    description: '',
  };
  protected newSavingsAccount = {
    name: '',
    color: '#2563eb',
  };
  protected showNewSavingsAccountForm = false;
  protected readonly savingTagSavingsLink = signal(false);
  protected tagSavingsLinkForm = {
    tagGroupId: 0,
    tagId: 0,
    savingsAccountId: 0,
  };

  private financialDataSubscription?: Subscription;

  constructor(private readonly api: FinanceApiService) {
    this.descriptionInput$
      .pipe(
        map((value) => value.trim()),
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((value) => {
          if (value.length < 2) {
            return of<TransactionSuggestion[]>([]);
          }

          const lowerValue = value.toLocaleLowerCase('ca');
          const fixedExpenseSuggestions: TransactionSuggestion[] = this.fixedExpenses()
            .filter((item) => item.description.toLocaleLowerCase('ca').includes(lowerValue))
            .slice(0, 5)
            .map((item) => ({
              kind: 'fixed-expense' as const,
              type: item.type,
              description: item.description,
              amount: item.amount,
              tags: item.tags,
              monthLabel: this.monthNames[item.month - 1],
            }));

          return this.api.searchTransactions(value).pipe(
            map((transactions): TransactionSuggestion[] => [
              ...fixedExpenseSuggestions,
              ...transactions.map((item) => ({
                kind: 'transaction' as const,
                type: item.type,
                description: item.description,
                amount: item.amount,
                tags: item.tags,
              })),
            ]),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((suggestions) => this.transactionSuggestions.set(suggestions));
  }

  ngOnInit(): void {
    this.setPeriodRange(this.period);
    this.loadAll();
  }

  ngOnDestroy(): void {
    this.financialDataSubscription?.unsubscribe();
  }

  protected loadAll(): void {
    this.loading.set(true);
    this.error.set('');

    this.api.getTagGroups().subscribe({
      next: (groups) => {
        this.tagGroups.set(groups);
        if (!this.newTag.tagGroupId && groups.length) {
          this.newTag.tagGroupId = groups[0].id;
        }
      },
      error: () => this.setConnectionError(),
    });

    this.refreshFinancialData();
    this.loadBudgets();
    this.loadFixedExpenses();
    this.loadWeeklyForecast();
    this.loadMonthlyFixedExpenses();
    this.loadSavingsAccounts();
  }

  protected refreshFinancialData(): void {
    this.financialDataSubscription?.unsubscribe();
    this.loading.set(true);
    this.error.set('');

    const tagIds = this.activeFilterTagIds();

    this.financialDataSubscription = this.api
      .getSummary(this.period, this.anchor, tagIds)
      .pipe(
        switchMap((summary) =>
          forkJoin({
            summary: of(summary),
            transactions: this.api.getTransactions(
              summary.from,
              summary.to,
              tagIds,
            ),
          }),
        ),
      )
      .subscribe({
        next: ({ summary, transactions }) => {
          this.summary.set(summary);
          this.periodFrom.set(summary.from);
          this.periodTo.set(summary.to);
          this.transactions.set(transactions);
          this.loading.set(false);
        },
        error: () => this.setConnectionError(),
      });
  }

  protected loadWeeklyForecast(): void {
    this.expandedWeekNumber = null;
    this.api.getWeeklyForecast(this.forecastAnchor).subscribe({
      next: (forecast) => this.weeklyForecast.set(forecast),
      error: () => this.weeklyForecast.set(null),
    });
  }

  protected toggleWeekDetail(weekNumber: number): void {
    this.expandedWeekNumber = this.expandedWeekNumber === weekNumber ? null : weekNumber;
  }

  protected navigateForecastPeriod(direction: -1 | 1): void {
    if (direction === 1 && this.isCurrentForecastPeriod()) {
      return;
    }

    const anchorDate = new Date(`${this.forecastAnchor}T12:00:00`);
    anchorDate.setDate(1);
    anchorDate.setMonth(anchorDate.getMonth() + direction);
    this.forecastAnchor = this.toLocalDate(anchorDate);
    this.loadWeeklyForecast();
  }

  protected goToCurrentForecastPeriod(): void {
    if (this.isCurrentForecastPeriod()) {
      return;
    }

    this.forecastAnchor = this.today();
    this.loadWeeklyForecast();
  }

  protected isCurrentForecastPeriod(): boolean {
    const today = new Date();
    const anchorDate = new Date(`${this.forecastAnchor}T12:00:00`);
    return today.getFullYear() === anchorDate.getFullYear()
      && today.getMonth() === anchorDate.getMonth();
  }

  protected forecastPeriodLabel(): string {
    const anchorDate = new Date(`${this.forecastAnchor}T12:00:00`);
    return `${this.monthNames[anchorDate.getMonth()]} ${anchorDate.getFullYear()}`;
  }

  protected toggleForecastSettings(): void {
    this.showForecastSettings = !this.showForecastSettings;
    if (this.showForecastSettings) {
      this.loadMonthlyFixedExpenses();
    }
  }

  protected loadMonthlyFixedExpenses(): void {
    this.api.getMonthlyFixedExpenses().subscribe({
      next: (items) => this.monthlyFixedExpenses.set(items),
      error: () => this.error.set('No s’han pogut carregar les despeses fixes mensuals.'),
    });
  }

  protected saveMonthlyFixedExpense(): void {
    if (!this.monthlyFixedExpenseForm.description.trim() || !this.monthlyFixedExpenseForm.amount) {
      this.error.set('Indica un concepte i un import superior a zero.');
      return;
    }

    this.savingMonthlyFixedExpense.set(true);
    this.error.set('');
    const request = {
      description: this.monthlyFixedExpenseForm.description.trim(),
      amount: this.monthlyFixedExpenseForm.amount,
      week: this.monthlyFixedExpenseForm.week,
    };
    const operation: Observable<unknown> = this.editingMonthlyFixedExpenseId
      ? this.api.updateMonthlyFixedExpense(this.editingMonthlyFixedExpenseId, request)
      : this.api.createMonthlyFixedExpense(request);

    operation.pipe(finalize(() => this.savingMonthlyFixedExpense.set(false)))
      .subscribe({
        next: () => {
          this.resetMonthlyFixedExpenseForm();
          this.loadMonthlyFixedExpenses();
          this.loadWeeklyForecast();
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut desar la despesa fixa mensual.',
          );
        },
      });
  }

  protected editMonthlyFixedExpense(item: MonthlyFixedExpense): void {
    this.editingMonthlyFixedExpenseId = item.id;
    this.monthlyFixedExpenseForm = {
      description: item.description,
      amount: item.amount,
      week: item.week,
    };
  }

  protected cancelMonthlyFixedExpenseEdit(): void {
    this.resetMonthlyFixedExpenseForm();
  }

  protected deleteMonthlyFixedExpense(id: number): void {
    this.api.deleteMonthlyFixedExpense(id).subscribe({
      next: () => {
        if (this.editingMonthlyFixedExpenseId === id) {
          this.resetMonthlyFixedExpenseForm();
        }
        this.loadMonthlyFixedExpenses();
        this.loadWeeklyForecast();
      },
      error: () => this.error.set('No s’ha pogut eliminar la despesa fixa mensual.'),
    });
  }

  private resetMonthlyFixedExpenseForm(): void {
    this.editingMonthlyFixedExpenseId = 0;
    this.monthlyFixedExpenseForm = {
      description: '',
      amount: null,
      week: 1,
    };
  }

  protected get forecastAnchorMonth(): number {
    return new Date(`${this.forecastAnchor}T12:00:00`).getMonth() + 1;
  }

  protected get fixedExpensesForForecastMonth(): FixedExpense[] {
    return this.fixedExpenses().filter((item) => item.month === this.forecastAnchorMonth);
  }

  protected get pendingFixedExpenseForecastCount(): number {
    return this.fixedExpensesForForecastMonth.filter((item) => item.forecastWeek === null).length;
  }

  protected assignFixedExpenseForecastWeek(fixedExpense: FixedExpense, week: number | null): void {
    this.savingFixedExpenseForecastId.set(fixedExpense.id);
    this.error.set('');
    this.api
      .saveFixedExpenseForecastWeek(fixedExpense.id, { week })
      .pipe(finalize(() => this.savingFixedExpenseForecastId.set(null)))
      .subscribe({
        next: () => {
          this.fixedExpenses.update((items) =>
            items.map((item) =>
              item.id === fixedExpense.id ? { ...item, forecastWeek: week } : item,
            ),
          );
          this.loadWeeklyForecast();
        },
        error: () => this.error.set('No s’ha pogut desar l’assignació de la despesa fixa.'),
      });
  }

  protected changePeriod(period: 'week' | 'month' | 'year'): void {
    this.notice.set('');
    this.period = period;
    this.anchor = this.today();
    this.setPeriodRange(period);
    this.refreshFinancialData();
  }

  protected showSection(section: 'consultation' | 'entry' | 'budgets' | 'fixed-expenses' | 'weekly-forecast' | 'savings'): void {
    this.activeSection = section;
    if (section === 'budgets') {
      this.loadBudgets();
    } else if (section === 'fixed-expenses') {
      this.loadFixedExpenses();
    } else if (section === 'weekly-forecast') {
      this.loadWeeklyForecast();
      this.loadFixedExpenses();
      this.loadMonthlyFixedExpenses();
    } else if (section === 'savings') {
      this.loadSavingsAccounts();
      this.loadSavingsMovements();
    }
  }

  protected backupDatabase(): void {
    this.backingUp.set(true);
    this.error.set('');
    this.notice.set('');

    this.api.backupDatabase()
      .pipe(finalize(() => this.backingUp.set(false)))
      .subscribe({
        next: (backup) => {
          this.notice.set(`Còpia de seguretat creada: ${backup.fileName}`);
          if (this.showBackupList) {
            this.loadBackups();
          }
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut crear la còpia de seguretat.',
          );
        },
      });
  }

  protected toggleBackupList(): void {
    this.showBackupList = !this.showBackupList;
    if (this.showBackupList) {
      this.loadBackups();
    }
  }

  protected loadBackups(): void {
    this.loadingBackups.set(true);
    this.api.getBackups()
      .pipe(finalize(() => this.loadingBackups.set(false)))
      .subscribe({
        next: (backups) => this.backups.set(backups),
        error: () => this.error.set('No s’han pogut carregar les còpies de seguretat.'),
      });
  }

  protected downloadBackup(fileName: string): void {
    this.downloadingBackupFileName.set(fileName);
    this.error.set('');
    this.api.downloadBackup(fileName)
      .pipe(finalize(() => this.downloadingBackupFileName.set(null)))
      .subscribe({
        next: (blob) => {
          const url = window.URL.createObjectURL(blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = fileName;
          link.click();
          window.URL.revokeObjectURL(url);
        },
        error: () => this.error.set('No s’ha pogut descarregar la còpia de seguretat.'),
      });
  }

  protected formatBackupSize(sizeBytes: number): string {
    if (sizeBytes < 1024 * 1024) {
      return `${Math.max(1, Math.round(sizeBytes / 1024))} KB`;
    }
    return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected get selectedBudget(): Budget | undefined {
    return this.budgets().find((budget) => budget.id === this.selectedBudgetId);
  }

  protected changeSelectedBudget(): void {
    this.showBudgetCopyForm = false;
    this.resetBudgetItemForm();
  }

  protected loadBudgets(selectId?: number): void {
    this.api.getBudgets().subscribe({
      next: (budgets) => {
        this.budgets.set(budgets);
        if (selectId) {
          this.selectedBudgetId = selectId;
        } else if (!budgets.some((item) => item.id === this.selectedBudgetId)) {
          this.selectedBudgetId = budgets[0]?.id ?? 0;
        }
      },
      error: () => this.error.set('No s’han pogut carregar els pressupostos.'),
    });
  }

  protected createBudget(): void {
    if (!this.budgetForm.name.trim()) {
      this.error.set('Escriu el nom del pressupost.');
      return;
    }

    this.savingBudget.set(true);
    this.error.set('');
    this.notice.set('');
    this.api.createBudget({ ...this.budgetForm, name: this.budgetForm.name.trim() })
      .pipe(finalize(() => this.savingBudget.set(false)))
      .subscribe({
        next: ({ id }) => {
          this.budgetForm.name = '';
          this.notice.set('Pressupost creat correctament.');
          this.loadBudgets(id);
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut crear el pressupost.',
          );
        },
      });
  }

  protected openBudgetCopy(): void {
    const budget = this.selectedBudget;
    if (!budget) return;

    const dates = this.nextBudgetDates(budget.from, budget.to);
    this.budgetCopyForm = {
      name: `${budget.name} (còpia)`,
      from: dates.from,
      to: dates.to,
    };
    this.showBudgetCopyForm = true;
  }

  protected cancelBudgetCopy(): void {
    this.showBudgetCopyForm = false;
  }

  protected copyBudget(): void {
    const budget = this.selectedBudget;
    if (!budget || !this.budgetCopyForm.name.trim()) {
      this.error.set('Indica el nom del nou pressupost.');
      return;
    }

    this.savingBudgetCopy.set(true);
    this.error.set('');
    this.notice.set('');
    this.api.copyBudget(budget.id, {
      ...this.budgetCopyForm,
      name: this.budgetCopyForm.name.trim(),
    }).pipe(finalize(() => this.savingBudgetCopy.set(false)))
      .subscribe({
        next: ({ id }) => {
          this.showBudgetCopyForm = false;
          this.notice.set('Pressupost copiat. Ja pots adaptar-ne els conceptes.');
          this.loadBudgets(id);
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut copiar el pressupost.',
          );
        },
      });
  }

  protected selectSingleBudgetTag(group: TagGroup, value: string): void {
    group.tags.forEach((tag) => this.selectedBudgetTagIds.delete(tag.id));
    if (value) {
      this.selectedBudgetTagIds.add(Number(value));
    }
  }

  protected selectedSingleBudgetTag(group: TagGroup): string {
    const selected = group.tags.find((tag) => this.selectedBudgetTagIds.has(tag.id));
    return selected ? String(selected.id) : '';
  }

  protected toggleBudgetTag(tagId: number, selected: boolean): void {
    if (selected) {
      this.selectedBudgetTagIds.add(tagId);
    } else {
      this.selectedBudgetTagIds.delete(tagId);
    }
  }

  protected saveBudgetItem(): void {
    const budget = this.selectedBudget;
    if (!budget || !this.budgetItemForm.description.trim() || !this.budgetItemForm.expectedAmount) {
      this.error.set('Selecciona un pressupost i indica un concepte i un import.');
      return;
    }

    this.savingBudgetItem.set(true);
    this.error.set('');
    this.notice.set('');
    const request = {
      type: this.budgetItemForm.type,
      description: this.budgetItemForm.description.trim(),
      expectedAmount: this.budgetItemForm.expectedAmount,
      tagIds: [...this.selectedBudgetTagIds],
    };
    const operation: Observable<unknown> = this.editingBudgetItemId
      ? this.api.updateBudgetItem(budget.id, this.editingBudgetItemId, request)
      : this.api.createBudgetItem(budget.id, request);

    operation.pipe(finalize(() => this.savingBudgetItem.set(false)))
      .subscribe({
        next: () => {
          this.notice.set(
            this.editingBudgetItemId
              ? 'Concepte actualitzat correctament.'
              : 'Concepte afegit al pressupost.',
          );
          this.resetBudgetItemForm();
          this.loadBudgets(budget.id);
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut guardar el concepte.',
          );
        },
      });
  }

  protected editBudgetItem(item: BudgetItem): void {
    this.editingBudgetItemId = item.id;
    this.budgetItemForm = {
      type: item.type,
      description: item.description,
      expectedAmount: item.expectedAmount,
    };
    this.selectedBudgetTagIds = new Set(item.tags.map((tag) => tag.id));
  }

  protected cancelBudgetItemEdit(): void {
    this.resetBudgetItemForm();
  }

  protected deleteBudgetItem(itemId: number): void {
    const budget = this.selectedBudget;
    if (!budget) return;
    this.api.deleteBudgetItem(budget.id, itemId).subscribe({
      next: () => {
        if (this.editingBudgetItemId === itemId) {
          this.resetBudgetItemForm();
        }
        this.loadBudgets(budget.id);
      },
      error: () => this.error.set('No s’ha pogut eliminar el concepte.'),
    });
  }

  protected deleteBudget(): void {
    const budget = this.selectedBudget;
    if (!budget || !window.confirm(`Vols eliminar el pressupost “${budget.name}” i tots els seus conceptes?`)) {
      return;
    }

    this.api.deleteBudget(budget.id).subscribe({
      next: () => {
        this.selectedBudgetId = 0;
        this.notice.set('Pressupost eliminat.');
        this.loadBudgets();
      },
      error: () => this.error.set('No s’ha pogut eliminar el pressupost.'),
    });
  }

  protected get fixedExpensesTotal(): { income: number; expense: number; balance: number } {
    const items = this.fixedExpenses();
    const income = items
      .filter((item) => item.type === 'income')
      .reduce((sum, item) => sum + item.amount, 0);
    const expense = items
      .filter((item) => item.type === 'expense')
      .reduce((sum, item) => sum + item.amount, 0);
    return { income, expense, balance: income - expense };
  }

  protected loadFixedExpenses(): void {
    this.api.getFixedExpenses().subscribe({
      next: (fixedExpenses) => this.fixedExpenses.set(fixedExpenses),
      error: () => this.error.set('No s’han pogut carregar les despeses fixes.'),
    });
  }

  protected selectSingleFixedExpenseTag(group: TagGroup, value: string): void {
    group.tags.forEach((tag) => this.selectedFixedExpenseTagIds.delete(tag.id));
    if (value) {
      this.selectedFixedExpenseTagIds.add(Number(value));
    }
  }

  protected selectedSingleFixedExpenseTag(group: TagGroup): string {
    const selected = group.tags.find((tag) => this.selectedFixedExpenseTagIds.has(tag.id));
    return selected ? String(selected.id) : '';
  }

  protected toggleFixedExpenseTag(tagId: number, selected: boolean): void {
    if (selected) {
      this.selectedFixedExpenseTagIds.add(tagId);
    } else {
      this.selectedFixedExpenseTagIds.delete(tagId);
    }
  }

  protected saveFixedExpense(): void {
    if (!this.fixedExpenseForm.description.trim() || !this.fixedExpenseForm.amount) {
      this.error.set('Indica un concepte i un import superior a zero.');
      return;
    }

    this.savingFixedExpense.set(true);
    this.error.set('');
    this.notice.set('');
    const request = {
      type: this.fixedExpenseForm.type,
      description: this.fixedExpenseForm.description.trim(),
      amount: this.fixedExpenseForm.amount,
      month: this.fixedExpenseForm.month,
      tagIds: [...this.selectedFixedExpenseTagIds],
    };
    const operation: Observable<unknown> = this.editingFixedExpenseId
      ? this.api.updateFixedExpense(this.editingFixedExpenseId, request)
      : this.api.createFixedExpense(request);

    operation.pipe(finalize(() => this.savingFixedExpense.set(false)))
      .subscribe({
        next: () => {
          this.notice.set(
            this.editingFixedExpenseId
              ? 'Despesa fixa actualitzada correctament.'
              : 'Despesa fixa afegida correctament.',
          );
          this.resetFixedExpenseForm();
          this.loadFixedExpenses();
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut guardar la despesa fixa.',
          );
        },
      });
  }

  protected editFixedExpense(item: FixedExpense): void {
    this.editingFixedExpenseId = item.id;
    this.fixedExpenseForm = {
      type: item.type,
      description: item.description,
      amount: item.amount,
      month: item.month,
    };
    this.selectedFixedExpenseTagIds = new Set(item.tags.map((tag) => tag.id));
  }

  protected cancelFixedExpenseEdit(): void {
    this.resetFixedExpenseForm();
  }

  protected deleteFixedExpense(id: number): void {
    this.api.deleteFixedExpense(id).subscribe({
      next: () => {
        if (this.editingFixedExpenseId === id) {
          this.resetFixedExpenseForm();
        }
        this.loadFixedExpenses();
      },
      error: () => this.error.set('No s’ha pogut eliminar la despesa fixa.'),
    });
  }

  protected get savingsTotalBalance(): number {
    return this.savingsAccounts().reduce((sum, account) => sum + account.balance, 0);
  }

  protected loadSavingsAccounts(): void {
    this.api.getSavingsAccounts(this.savingsYear).subscribe({
      next: (accounts) => {
        this.savingsAccounts.set(accounts);
        if (!this.savingsMovementForm.savingsAccountId && accounts.length) {
          this.savingsMovementForm.savingsAccountId = accounts[0].id;
        }
      },
      error: () => this.error.set('No s’han pogut carregar els comptes d’estalvi.'),
    });
  }

  protected loadSavingsMovements(): void {
    const accountId = this.savingsMovementFilterAccountId || undefined;
    this.api.getSavingsMovements(accountId, this.savingsYear).subscribe({
      next: (movements) => this.savingsMovements.set(movements),
      error: () => this.error.set('No s’han pogut carregar els moviments d’estalvi.'),
    });
  }

  protected changeSavingsMovementFilter(): void {
    this.loadSavingsMovements();
  }

  protected navigateSavingsYear(direction: -1 | 1): void {
    if (direction === 1 && this.isCurrentSavingsYear()) {
      return;
    }

    this.savingsYear += direction;
    this.loadSavingsAccounts();
    this.loadSavingsMovements();
  }

  protected goToCurrentSavingsYear(): void {
    if (this.isCurrentSavingsYear()) {
      return;
    }

    this.savingsYear = new Date().getFullYear();
    this.loadSavingsAccounts();
    this.loadSavingsMovements();
  }

  protected isCurrentSavingsYear(): boolean {
    return this.savingsYear === new Date().getFullYear();
  }

  protected saveSavingsMovement(): void {
    if (
      !this.savingsMovementForm.savingsAccountId ||
      !this.savingsMovementForm.amount ||
      !this.savingsMovementForm.description.trim()
    ) {
      this.error.set('Selecciona un compte i indica un concepte i un import superior a zero.');
      return;
    }

    this.savingSavingsMovement.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .createSavingsMovement({
        ...this.savingsMovementForm,
        amount: this.savingsMovementForm.amount,
        description: this.savingsMovementForm.description.trim(),
      })
      .pipe(finalize(() => this.savingSavingsMovement.set(false)))
      .subscribe({
        next: () => {
          const accountId = this.savingsMovementForm.savingsAccountId;
          this.savingsMovementForm = {
            savingsAccountId: accountId,
            type: 'deposit',
            amount: null,
            date: this.today(),
            description: '',
          };
          this.notice.set('Moviment d’estalvi guardat correctament.');
          this.loadSavingsAccounts();
          this.loadSavingsMovements();
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut guardar el moviment d’estalvi.',
          );
        },
      });
  }

  protected deleteSavingsMovement(id: number): void {
    this.api.deleteSavingsMovement(id).subscribe({
      next: () => {
        this.loadSavingsAccounts();
        this.loadSavingsMovements();
      },
      error: () => this.error.set('No s’ha pogut eliminar el moviment d’estalvi.'),
    });
  }

  protected toggleNewSavingsAccountForm(): void {
    this.showNewSavingsAccountForm = !this.showNewSavingsAccountForm;
  }

  protected createSavingsAccount(): void {
    if (!this.newSavingsAccount.name.trim()) {
      this.error.set('Escriu el nom del compte d’estalvi.');
      return;
    }

    this.savingSavingsAccount.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .createSavingsAccount({
        ...this.newSavingsAccount,
        name: this.newSavingsAccount.name.trim(),
      })
      .pipe(finalize(() => this.savingSavingsAccount.set(false)))
      .subscribe({
        next: () => {
          this.newSavingsAccount = { name: '', color: '#2563eb' };
          this.showNewSavingsAccountForm = false;
          this.notice.set('Compte d’estalvi creat correctament.');
          this.loadSavingsAccounts();
        },
        error: () => this.error.set('No s’ha pogut crear el compte d’estalvi.'),
      });
  }

  protected deleteSavingsAccount(account: SavingsAccount): void {
    if (!window.confirm(`Vols eliminar el compte “${account.name}” i tots els seus moviments?`)) {
      return;
    }

    this.api.deleteSavingsAccount(account.id).subscribe({
      next: () => {
        if (this.savingsMovementFilterAccountId === account.id) {
          this.savingsMovementFilterAccountId = 0;
        }
        this.notice.set('Compte d’estalvi eliminat.');
        this.loadSavingsAccounts();
        this.loadSavingsMovements();
      },
      error: () => this.error.set('No s’ha pogut eliminar el compte d’estalvi.'),
    });
  }

  protected get linkedSavingsTags(): { tag: TransactionTag; accountName: string }[] {
    const accountNamesById = new Map(this.savingsAccounts().map((account) => [account.id, account.name]));
    const result: { tag: TransactionTag; accountName: string }[] = [];
    for (const group of this.tagGroups()) {
      for (const tag of group.tags) {
        if (tag.linkedSavingsAccountId) {
          result.push({
            tag: {
              id: tag.id,
              groupId: group.id,
              groupName: group.name,
              name: tag.name,
              color: tag.color,
            },
            accountName: accountNamesById.get(tag.linkedSavingsAccountId) ?? '—',
          });
        }
      }
    }
    return result;
  }

  protected get tagSavingsLinkGroupTags() {
    return this.tagGroups().find((group) => group.id === this.tagSavingsLinkForm.tagGroupId)?.tags ?? [];
  }

  protected saveTagSavingsLink(): void {
    if (!this.tagSavingsLinkForm.tagId || !this.tagSavingsLinkForm.savingsAccountId) {
      this.error.set('Selecciona una categoria i un compte d’estalvi.');
      return;
    }

    this.savingTagSavingsLink.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .saveTagSavingsLink(this.tagSavingsLinkForm.tagId, {
        savingsAccountId: this.tagSavingsLinkForm.savingsAccountId,
      })
      .pipe(finalize(() => this.savingTagSavingsLink.set(false)))
      .subscribe({
        next: () => {
          this.tagSavingsLinkForm = { tagGroupId: 0, tagId: 0, savingsAccountId: 0 };
          this.notice.set('Categoria vinculada correctament.');
          this.refreshSavingsAndTags();
        },
        error: () => this.error.set('No s’ha pogut vincular la categoria.'),
      });
  }

  protected removeTagSavingsLink(tagId: number): void {
    this.api.saveTagSavingsLink(tagId, { savingsAccountId: null }).subscribe({
      next: () => {
        this.notice.set('Vinculació eliminada.');
        this.refreshSavingsAndTags();
      },
      error: () => this.error.set('No s’ha pogut eliminar la vinculació.'),
    });
  }

  private refreshSavingsAndTags(): void {
    this.api.getTagGroups().subscribe({
      next: (groups) => this.tagGroups.set(groups),
      error: () => this.setConnectionError(),
    });
    this.loadSavingsAccounts();
    this.loadSavingsMovements();
  }

  protected navigatePeriod(direction: -1 | 1): void {
    if (direction === 1 && this.isCurrentPeriod()) {
      return;
    }

    const anchorDate = new Date(`${this.anchor}T12:00:00`);
    if (this.period === 'week') {
      anchorDate.setDate(anchorDate.getDate() + direction * 7);
    } else if (this.period === 'month') {
      anchorDate.setDate(1);
      anchorDate.setMonth(anchorDate.getMonth() + direction);
    } else {
      anchorDate.setMonth(0, 1);
      anchorDate.setFullYear(anchorDate.getFullYear() + direction);
    }

    this.anchor = this.toLocalDate(anchorDate);
    this.setPeriodRange(this.period);
    this.refreshFinancialData();
  }

  protected goToCurrentPeriod(): void {
    if (this.isCurrentPeriod()) {
      return;
    }

    this.anchor = this.today();
    this.setPeriodRange(this.period);
    this.refreshFinancialData();
  }

  protected isCurrentPeriod(): boolean {
    const today = this.today();
    return this.periodFrom() <= today && today <= this.periodTo();
  }

  protected get typeGroup(): TagGroup | undefined {
    return this.tagGroups().find(
      (group) => group.name.toLocaleLowerCase('ca') === 'tipus',
    );
  }

  protected get subtypeGroup(): TagGroup | undefined {
    return this.tagGroups().find(
      (group) => group.name.toLocaleLowerCase('ca') === 'subtipus',
    );
  }

  protected changeTagFilters(): void {
    this.refreshFinancialData();
  }

  protected clearTagFilters(): void {
    this.typeFilterId = 0;
    this.subtypeFilterId = 0;
    this.refreshFinancialData();
  }

  protected selectSingleTag(group: TagGroup, value: string): void {
    group.tags.forEach((tag) => this.selectedTagIds.delete(tag.id));
    if (value) {
      this.selectedTagIds.add(Number(value));
    }
  }

  protected selectedSingleTag(group: TagGroup): string {
    const selected = group.tags.find((tag) => this.selectedTagIds.has(tag.id));
    return selected ? String(selected.id) : '';
  }

  protected toggleTag(tagId: number, selected: boolean): void {
    if (selected) {
      this.selectedTagIds.add(tagId);
    } else {
      this.selectedTagIds.delete(tagId);
    }
  }

  protected saveTransaction(): void {
    if (
      !this.transactionForm.amount ||
      !this.transactionForm.description.trim()
    ) {
      this.error.set('Indica una descripció i un import superior a zero.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .createTransaction({
        ...this.transactionForm,
        amount: this.transactionForm.amount,
        description: this.transactionForm.description.trim(),
        tagIds: [...this.selectedTagIds],
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.transactionForm = {
            type: 'expense',
            amount: null,
            date: this.today(),
            description: '',
          };
          this.transactionAmountText = '';
          this.selectedTagIds.clear();
          this.transactionSuggestions.set([]);
          this.notice.set('Moviment guardat correctament.');
          this.refreshFinancialData();
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut guardar el moviment.',
          );
        },
      });
  }

  protected onDescriptionInput(value: string): void {
    this.descriptionInput$.next(value);
  }

  protected hideSuggestionsSoon(): void {
    setTimeout(() => this.transactionSuggestions.set([]), 150);
  }

  protected onAmountInput(value: string): void {
    this.transactionAmountText = value;
    this.transactionForm.amount = this.parseAmount(value);
  }

  protected applyTransactionSuggestion(suggestion: TransactionSuggestion): void {
    this.transactionForm = {
      ...this.transactionForm,
      type: suggestion.type,
      description: suggestion.description,
      amount: suggestion.amount,
    };
    this.transactionAmountText = this.formatAmount(suggestion.amount);
    this.selectedTagIds = new Set(suggestion.tags.map((tag) => tag.id));
    this.transactionSuggestions.set([]);
  }

  protected deleteTransaction(id: number): void {
    this.api.deleteTransaction(id).subscribe({
      next: () => this.refreshFinancialData(),
      error: () => this.error.set('No s’ha pogut eliminar el moviment.'),
    });
  }

  protected createTag(): void {
    if (!this.newTag.tagGroupId || !this.newTag.name.trim()) {
      this.error.set('Selecciona un grup i escriu el nom del tag.');
      return;
    }

    this.savingTag.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .createTag({
        ...this.newTag,
        name: this.newTag.name.trim(),
        parentTagId: null,
      })
      .pipe(finalize(() => this.savingTag.set(false)))
      .subscribe({
        next: () => {
          this.newTag.name = '';
          this.notice.set('Tag creat correctament.');
          this.loadAll();
        },
        error: () => this.error.set('No s’ha pogut crear el tag.'),
      });
  }

  protected formatCurrency(value: number): string {
    return new Intl.NumberFormat('ca-ES', {
      style: 'currency',
      currency: 'EUR',
    }).format(value);
  }

  protected periodLabel(): string {
    const summary = this.summary();
    if (!summary) {
      return '';
    }

    return `${this.formatDate(summary.from)} – ${this.formatDate(
      summary.to,
    )}`;
  }

  protected formatDate(value: string): string {
    return new Intl.DateTimeFormat('ca-ES', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    }).format(new Date(`${value}T12:00:00`));
  }

  private setConnectionError(): void {
    this.loading.set(false);
    this.error.set(
      'No es pot connectar amb l’API. Comprova que el servidor estigui iniciat.',
    );
  }

  private activeFilterTagIds(): number[] {
    return [this.typeFilterId, this.subtypeFilterId].filter(
      (tagId) => tagId > 0,
    );
  }

  private setPeriodRange(period: 'week' | 'month' | 'year'): void {
    const today = new Date(`${this.anchor}T12:00:00`);
    let from: Date;
    let to: Date;

    if (period === 'week') {
      const daysFromMonday = (today.getDay() + 6) % 7;
      from = new Date(today);
      from.setDate(today.getDate() - daysFromMonday);
      to = new Date(from);
      to.setDate(from.getDate() + 6);
    } else if (period === 'month') {
      from = new Date(today.getFullYear(), today.getMonth(), 1, 12);
      to = new Date(today.getFullYear(), today.getMonth() + 1, 0, 12);
    } else {
      from = new Date(today.getFullYear(), 0, 1, 12);
      to = new Date(today.getFullYear(), 11, 31, 12);
    }

    this.periodFrom.set(this.toLocalDate(from));
    this.periodTo.set(this.toLocalDate(to));
  }

  private formatAmount(value: number): string {
    return String(value).replace('.', ',');
  }

  private parseAmount(value: string): number | null {
    const normalized = value.trim().replace(',', '.');
    if (!normalized) {
      return null;
    }

    const parsed = Number(normalized);
    return Number.isFinite(parsed) ? parsed : null;
  }

  private toLocalDate(date: Date): string {
    const timezoneOffset = date.getTimezoneOffset() * 60_000;
    return new Date(date.getTime() - timezoneOffset)
      .toISOString()
      .slice(0, 10);
  }

  private today(): string {
    const date = new Date();
    const timezoneOffset = date.getTimezoneOffset() * 60_000;
    return new Date(date.getTime() - timezoneOffset)
      .toISOString()
      .slice(0, 10);
  }

  private resetBudgetItemForm(): void {
    this.editingBudgetItemId = 0;
    this.budgetItemForm = {
      type: 'expense',
      description: '',
      expectedAmount: null,
    };
    this.selectedBudgetTagIds.clear();
  }

  private resetFixedExpenseForm(): void {
    this.editingFixedExpenseId = 0;
    this.fixedExpenseForm = {
      type: 'expense',
      description: '',
      amount: null,
      month: new Date().getMonth() + 1,
    };
    this.selectedFixedExpenseTagIds.clear();
  }

  private nextBudgetDates(fromValue: string, toValue: string): { from: string; to: string } {
    const from = new Date(`${fromValue}T12:00:00`);
    const to = new Date(`${toValue}T12:00:00`);
    const lastDayOfSourceMonth = new Date(from.getFullYear(), from.getMonth() + 1, 0).getDate();
    const isFullMonth = from.getDate() === 1
      && to.getFullYear() === from.getFullYear()
      && to.getMonth() === from.getMonth()
      && to.getDate() === lastDayOfSourceMonth;

    if (isFullMonth) {
      const nextFrom = new Date(from.getFullYear(), from.getMonth() + 1, 1, 12);
      const nextTo = new Date(from.getFullYear(), from.getMonth() + 2, 0, 12);
      return { from: this.toLocalDate(nextFrom), to: this.toLocalDate(nextTo) };
    }

    const duration = to.getTime() - from.getTime();
    const nextFrom = new Date(to);
    nextFrom.setDate(nextFrom.getDate() + 1);
    const nextTo = new Date(nextFrom.getTime() + duration);
    return { from: this.toLocalDate(nextFrom), to: this.toLocalDate(nextTo) };
  }

  private firstDayOfCurrentMonth(): string {
    const now = new Date();
    return this.toLocalDate(new Date(now.getFullYear(), now.getMonth(), 1, 12));
  }

  private lastDayOfCurrentMonth(): string {
    const now = new Date();
    return this.toLocalDate(new Date(now.getFullYear(), now.getMonth() + 1, 0, 12));
  }
}
