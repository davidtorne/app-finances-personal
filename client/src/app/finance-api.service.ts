import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import {
  BackupResult,
  Budget,
  CreateBudget,
  CreateBudgetItem,
  CreateFixedExpense,
  CreateMonthlyFixedExpense,
  CreateSavingsAccount,
  CreateSavingsMovement,
  CreateTag,
  CreateTransaction,
  FinanceSummary,
  FinanceTransaction,
  FixedExpense,
  MonthlyFixedExpense,
  SaveFixedExpenseForecastWeek,
  SaveTagSavingsLink,
  SavingsAccount,
  SavingsMovement,
  SummaryPeriod,
  TagGroup,
  WeeklyForecast,
} from './finance.models';

@Injectable({ providedIn: 'root' })
export class FinanceApiService {
  private readonly baseUrl = '/api';

  constructor(private readonly http: HttpClient) {}

  getTagGroups() {
    return this.http.get<TagGroup[]>(`${this.baseUrl}/tag-groups`);
  }

  getTransactions(from?: string, to?: string, tagIds: number[] = []) {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    tagIds.forEach((tagId) => {
      params = params.append('tagIds', String(tagId));
    });

    return this.http.get<FinanceTransaction[]>(
      `${this.baseUrl}/transactions/`,
      { params },
    );
  }

  searchTransactions(query: string, limit = 6) {
    const params = new HttpParams().set('q', query).set('limit', limit);

    return this.http.get<FinanceTransaction[]>(
      `${this.baseUrl}/transactions/search`,
      { params },
    );
  }

  createTransaction(transaction: CreateTransaction) {
    return this.http.post<FinanceTransaction>(
      `${this.baseUrl}/transactions/`,
      transaction,
    );
  }

  deleteTransaction(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/transactions/${id}`);
  }

  getSummary(period: SummaryPeriod, anchor: string, tagIds: number[] = []) {
    let params = new HttpParams()
      .set('period', period)
      .set('anchor', anchor);
    tagIds.forEach((tagId) => {
      params = params.append('tagIds', String(tagId));
    });

    return this.http.get<FinanceSummary>(`${this.baseUrl}/summary`, {
      params,
    });
  }

  getWeeklyForecast(anchor: string) {
    const params = new HttpParams().set('anchor', anchor);
    return this.http.get<WeeklyForecast>(`${this.baseUrl}/weekly-forecast`, { params });
  }

  getMonthlyFixedExpenses() {
    return this.http.get<MonthlyFixedExpense[]>(`${this.baseUrl}/monthly-fixed-expenses/`);
  }

  createMonthlyFixedExpense(monthlyFixedExpense: CreateMonthlyFixedExpense) {
    return this.http.post<{ id: number }>(
      `${this.baseUrl}/monthly-fixed-expenses/`,
      monthlyFixedExpense,
    );
  }

  updateMonthlyFixedExpense(id: number, monthlyFixedExpense: CreateMonthlyFixedExpense) {
    return this.http.put<void>(
      `${this.baseUrl}/monthly-fixed-expenses/${id}`,
      monthlyFixedExpense,
    );
  }

  deleteMonthlyFixedExpense(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/monthly-fixed-expenses/${id}`);
  }

  createTag(tag: CreateTag) {
    return this.http.post<number>(`${this.baseUrl}/tags`, tag);
  }

  saveTagSavingsLink(tagId: number, link: SaveTagSavingsLink) {
    return this.http.put<void>(`${this.baseUrl}/tags/${tagId}/savings-link`, link);
  }

  getBudgets() {
    return this.http.get<Budget[]>(`${this.baseUrl}/budgets/`);
  }

  createBudget(budget: CreateBudget) {
    return this.http.post<{ id: number }>(`${this.baseUrl}/budgets/`, budget);
  }

  copyBudget(budgetId: number, budget: CreateBudget) {
    return this.http.post<{ id: number }>(
      `${this.baseUrl}/budgets/${budgetId}/copy`,
      budget,
    );
  }

  createBudgetItem(budgetId: number, item: CreateBudgetItem) {
    return this.http.post<{ id: number }>(
      `${this.baseUrl}/budgets/${budgetId}/items`,
      item,
    );
  }

  updateBudgetItem(budgetId: number, itemId: number, item: CreateBudgetItem) {
    return this.http.put<void>(
      `${this.baseUrl}/budgets/${budgetId}/items/${itemId}`,
      item,
    );
  }

  deleteBudgetItem(budgetId: number, itemId: number) {
    return this.http.delete<void>(
      `${this.baseUrl}/budgets/${budgetId}/items/${itemId}`,
    );
  }

  deleteBudget(budgetId: number) {
    return this.http.delete<void>(`${this.baseUrl}/budgets/${budgetId}`);
  }

  getFixedExpenses() {
    return this.http.get<FixedExpense[]>(`${this.baseUrl}/fixed-expenses/`);
  }

  createFixedExpense(fixedExpense: CreateFixedExpense) {
    return this.http.post<{ id: number }>(
      `${this.baseUrl}/fixed-expenses/`,
      fixedExpense,
    );
  }

  updateFixedExpense(id: number, fixedExpense: CreateFixedExpense) {
    return this.http.put<void>(
      `${this.baseUrl}/fixed-expenses/${id}`,
      fixedExpense,
    );
  }

  deleteFixedExpense(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/fixed-expenses/${id}`);
  }

  saveFixedExpenseForecastWeek(id: number, forecastWeek: SaveFixedExpenseForecastWeek) {
    return this.http.put<void>(
      `${this.baseUrl}/fixed-expenses/${id}/forecast-week`,
      forecastWeek,
    );
  }

  getSavingsAccounts(year?: number) {
    let params = new HttpParams();
    if (year) {
      params = params.set('year', year);
    }

    return this.http.get<SavingsAccount[]>(`${this.baseUrl}/savings/accounts`, { params });
  }

  createSavingsAccount(account: CreateSavingsAccount) {
    return this.http.post<{ id: number }>(`${this.baseUrl}/savings/accounts`, account);
  }

  deleteSavingsAccount(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/savings/accounts/${id}`);
  }

  getSavingsMovements(accountId?: number, year?: number) {
    let params = new HttpParams();
    if (accountId) {
      params = params.set('accountId', accountId);
    }
    if (year) {
      params = params.set('year', year);
    }

    return this.http.get<SavingsMovement[]>(`${this.baseUrl}/savings/movements`, { params });
  }

  createSavingsMovement(movement: CreateSavingsMovement) {
    return this.http.post<{ id: number }>(`${this.baseUrl}/savings/movements`, movement);
  }

  deleteSavingsMovement(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/savings/movements/${id}`);
  }

  backupDatabase() {
    return this.http.post<BackupResult>(`${this.baseUrl}/backups/database`, {});
  }

  getBackups() {
    return this.http.get<BackupResult[]>(`${this.baseUrl}/backups/`);
  }

  downloadBackup(fileName: string) {
    return this.http.get(`${this.baseUrl}/backups/${encodeURIComponent(fileName)}/download`, {
      responseType: 'blob',
    });
  }
}
