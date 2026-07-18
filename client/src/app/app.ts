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
  Budget,
  BudgetItem,
  FinanceSummary,
  FinanceTransaction,
  FixedExpense,
  TagGroup,
  TransactionType,
} from './finance.models';

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit, OnDestroy {
  protected activeSection: 'consultation' | 'entry' | 'budgets' | 'fixed-expenses' = 'consultation';
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

  protected transactionForm = {
    type: 'expense' as TransactionType,
    amount: null as number | null,
    date: this.today(),
    description: '',
  };

  protected selectedTagIds = new Set<number>();
  protected readonly transactionSuggestions = signal<FinanceTransaction[]>([]);
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

  private financialDataSubscription?: Subscription;

  constructor(private readonly api: FinanceApiService) {
    this.descriptionInput$
      .pipe(
        map((value) => value.trim()),
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((value) =>
          value.length >= 2 ? this.api.searchTransactions(value) : of([]),
        ),
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

  protected changePeriod(period: 'week' | 'month' | 'year'): void {
    this.notice.set('');
    this.period = period;
    this.anchor = this.today();
    this.setPeriodRange(period);
    this.refreshFinancialData();
  }

  protected showSection(section: 'consultation' | 'entry' | 'budgets' | 'fixed-expenses'): void {
    this.activeSection = section;
    if (section === 'budgets') {
      this.loadBudgets();
    } else if (section === 'fixed-expenses') {
      this.loadFixedExpenses();
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
          this.notice.set(`Copia de seguretat creada: ${backup.relativePath}`);
        },
        error: (response) => {
          this.error.set(
            typeof response.error === 'string'
              ? response.error
              : 'No s ha pogut crear la copia de seguretat.',
          );
        },
      });
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

  protected applyTransactionSuggestion(suggestion: FinanceTransaction): void {
    this.transactionForm = {
      ...this.transactionForm,
      type: suggestion.type,
      description: suggestion.description,
      amount: suggestion.amount,
    };
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
