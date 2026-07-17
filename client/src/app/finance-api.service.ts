import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import {
  BackupResult,
  Budget,
  CreateBudget,
  CreateBudgetItem,
  CreateTag,
  CreateTransaction,
  FinanceSummary,
  FinanceTransaction,
  SummaryPeriod,
  TagGroup,
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

  createTag(tag: CreateTag) {
    return this.http.post<number>(`${this.baseUrl}/tags`, tag);
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

  backupDatabase() {
    return this.http.post<BackupResult>(`${this.baseUrl}/backups/database`, {});
  }
}
