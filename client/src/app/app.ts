import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize, forkJoin, Observable, of, Subscription, switchMap } from 'rxjs';
import { FinanceApiService } from './finance-api.service';
import {
  Budget,
  BudgetItem,
  FinanceSummary,
  FinanceTransaction,
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
  protected activeSection: 'consultation' | 'entry' | 'budgets' = 'consultation';
  protected summary: FinanceSummary | null = null;
  protected transactions: FinanceTransaction[] = [];
  protected tagGroups: TagGroup[] = [];
  protected period: 'week' | 'month' | 'year' = 'month';
  protected anchor = this.today();
  protected periodFrom = '';
  protected periodTo = '';
  protected typeFilterId = 0;
  protected subtypeFilterId = 0;
  protected loading = true;
  protected saving = false;
  protected savingTag = false;
  protected savingBudget = false;
  protected savingBudgetCopy = false;
  protected savingBudgetItem = false;
  protected backingUp = false;
  protected error = '';
  protected notice = '';

  protected transactionForm = {
    type: 'expense' as TransactionType,
    amount: null as number | null,
    date: this.today(),
    description: '',
  };

  protected selectedTagIds = new Set<number>();
  protected newTag = {
    tagGroupId: 0,
    name: '',
    color: '#2563eb',
  };
  protected budgets: Budget[] = [];
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

  private financialDataSubscription?: Subscription;

  constructor(private readonly api: FinanceApiService) {}

  ngOnInit(): void {
    this.setPeriodRange(this.period);
    this.loadAll();
  }

  ngOnDestroy(): void {
    this.financialDataSubscription?.unsubscribe();
  }

  protected loadAll(): void {
    this.loading = true;
    this.error = '';

    this.api.getTagGroups().subscribe({
      next: (groups) => {
        this.tagGroups = groups;
        if (!this.newTag.tagGroupId && groups.length) {
          this.newTag.tagGroupId = groups[0].id;
        }
      },
      error: () => this.setConnectionError(),
    });

    this.refreshFinancialData();
    this.loadBudgets();
  }

  protected refreshFinancialData(): void {
    this.financialDataSubscription?.unsubscribe();
    this.loading = true;
    this.error = '';

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
          this.summary = summary;
          this.periodFrom = summary.from;
          this.periodTo = summary.to;
          this.transactions = transactions;
          this.loading = false;
        },
        error: () => this.setConnectionError(),
      });
  }

  protected changePeriod(period: 'week' | 'month' | 'year'): void {
    this.notice = '';
    this.period = period;
    this.anchor = this.today();
    this.setPeriodRange(period);
    this.refreshFinancialData();
  }

  protected showSection(section: 'consultation' | 'entry' | 'budgets'): void {
    this.activeSection = section;
    if (section === 'budgets') {
      this.loadBudgets();
    }
  }

  protected backupDatabase(): void {
    this.backingUp = true;
    this.error = '';
    this.notice = '';

    this.api.backupDatabase()
      .pipe(finalize(() => (this.backingUp = false)))
      .subscribe({
        next: (backup) => {
          this.notice = `Copia de seguretat creada: ${backup.relativePath}`;
        },
        error: (response) => {
          this.error = typeof response.error === 'string'
            ? response.error
            : 'No s ha pogut crear la copia de seguretat.';
        },
      });
  }

  protected get selectedBudget(): Budget | undefined {
    return this.budgets.find((budget) => budget.id === this.selectedBudgetId);
  }

  protected changeSelectedBudget(): void {
    this.showBudgetCopyForm = false;
    this.resetBudgetItemForm();
  }

  protected loadBudgets(selectId?: number): void {
    this.api.getBudgets().subscribe({
      next: (budgets) => {
        this.budgets = budgets;
        if (selectId) {
          this.selectedBudgetId = selectId;
        } else if (!budgets.some((item) => item.id === this.selectedBudgetId)) {
          this.selectedBudgetId = budgets[0]?.id ?? 0;
        }
      },
      error: () => (this.error = 'No s’han pogut carregar els pressupostos.'),
    });
  }

  protected createBudget(): void {
    if (!this.budgetForm.name.trim()) {
      this.error = 'Escriu el nom del pressupost.';
      return;
    }

    this.savingBudget = true;
    this.error = '';
    this.notice = '';
    this.api.createBudget({ ...this.budgetForm, name: this.budgetForm.name.trim() })
      .pipe(finalize(() => (this.savingBudget = false)))
      .subscribe({
        next: ({ id }) => {
          this.budgetForm.name = '';
          this.notice = 'Pressupost creat correctament.';
          this.loadBudgets(id);
        },
        error: (response) => {
          this.error = typeof response.error === 'string'
            ? response.error
            : 'No s’ha pogut crear el pressupost.';
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
      this.error = 'Indica el nom del nou pressupost.';
      return;
    }

    this.savingBudgetCopy = true;
    this.error = '';
    this.notice = '';
    this.api.copyBudget(budget.id, {
      ...this.budgetCopyForm,
      name: this.budgetCopyForm.name.trim(),
    }).pipe(finalize(() => (this.savingBudgetCopy = false)))
      .subscribe({
        next: ({ id }) => {
          this.showBudgetCopyForm = false;
          this.notice = 'Pressupost copiat. Ja pots adaptar-ne els conceptes.';
          this.loadBudgets(id);
        },
        error: (response) => {
          this.error = typeof response.error === 'string'
            ? response.error
            : 'No s’ha pogut copiar el pressupost.';
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
      this.error = 'Selecciona un pressupost i indica un concepte i un import.';
      return;
    }

    this.savingBudgetItem = true;
    this.error = '';
    this.notice = '';
    const request = {
      type: this.budgetItemForm.type,
      description: this.budgetItemForm.description.trim(),
      expectedAmount: this.budgetItemForm.expectedAmount,
      tagIds: [...this.selectedBudgetTagIds],
    };
    const operation: Observable<unknown> = this.editingBudgetItemId
      ? this.api.updateBudgetItem(budget.id, this.editingBudgetItemId, request)
      : this.api.createBudgetItem(budget.id, request);

    operation.pipe(finalize(() => (this.savingBudgetItem = false)))
      .subscribe({
        next: () => {
          this.notice = this.editingBudgetItemId
            ? 'Concepte actualitzat correctament.'
            : 'Concepte afegit al pressupost.';
          this.resetBudgetItemForm();
          this.loadBudgets(budget.id);
        },
        error: (response) => {
          this.error = typeof response.error === 'string'
            ? response.error
            : 'No s’ha pogut guardar el concepte.';
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
      error: () => (this.error = 'No s’ha pogut eliminar el concepte.'),
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
        this.notice = 'Pressupost eliminat.';
        this.loadBudgets();
      },
      error: () => (this.error = 'No s’ha pogut eliminar el pressupost.'),
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
    return this.periodFrom <= today && today <= this.periodTo;
  }

  protected get typeGroup(): TagGroup | undefined {
    return this.tagGroups.find(
      (group) => group.name.toLocaleLowerCase('ca') === 'tipus',
    );
  }

  protected get subtypeGroup(): TagGroup | undefined {
    return this.tagGroups.find(
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
      this.error = 'Indica una descripció i un import superior a zero.';
      return;
    }

    this.saving = true;
    this.error = '';
    this.notice = '';
    this.api
      .createTransaction({
        ...this.transactionForm,
        amount: this.transactionForm.amount,
        description: this.transactionForm.description.trim(),
        tagIds: [...this.selectedTagIds],
      })
      .pipe(finalize(() => (this.saving = false)))
      .subscribe({
        next: () => {
          this.transactionForm = {
            type: 'expense',
            amount: null,
            date: this.today(),
            description: '',
          };
          this.selectedTagIds.clear();
          this.notice = 'Moviment guardat correctament.';
          this.refreshFinancialData();
        },
        error: (response) => {
          this.error =
            typeof response.error === 'string'
              ? response.error
              : 'No s’ha pogut guardar el moviment.';
        },
      });
  }

  protected deleteTransaction(id: number): void {
    this.api.deleteTransaction(id).subscribe({
      next: () => this.refreshFinancialData(),
      error: () => (this.error = 'No s’ha pogut eliminar el moviment.'),
    });
  }

  protected createTag(): void {
    if (!this.newTag.tagGroupId || !this.newTag.name.trim()) {
      this.error = 'Selecciona un grup i escriu el nom del tag.';
      return;
    }

    this.savingTag = true;
    this.error = '';
    this.notice = '';
    this.api
      .createTag({
        ...this.newTag,
        name: this.newTag.name.trim(),
        parentTagId: null,
      })
      .pipe(finalize(() => (this.savingTag = false)))
      .subscribe({
        next: () => {
          this.newTag.name = '';
          this.notice = 'Tag creat correctament.';
          this.loadAll();
        },
        error: () => (this.error = 'No s’ha pogut crear el tag.'),
      });
  }

  protected formatCurrency(value: number): string {
    return new Intl.NumberFormat('ca-ES', {
      style: 'currency',
      currency: 'EUR',
    }).format(value);
  }

  protected periodLabel(): string {
    if (!this.summary) {
      return '';
    }

    return `${this.formatDate(this.summary.from)} – ${this.formatDate(
      this.summary.to,
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
    this.loading = false;
    this.error =
      'No es pot connectar amb l’API. Comprova que el servidor estigui iniciat.';
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

    this.periodFrom = this.toLocalDate(from);
    this.periodTo = this.toLocalDate(to);
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
