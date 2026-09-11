export type TransactionType = 'income' | 'expense';
export type SummaryPeriod = 'week' | 'month' | 'year';

export interface Tag {
  id: number;
  name: string;
  color: string;
  parentTagId: number | null;
  linkedSavingsAccountId: number | null;
}

export interface TagGroup {
  id: number;
  name: string;
  selectionMode: 'single' | 'multiple';
  isRequired: boolean;
  tags: Tag[];
}

export interface TransactionTag {
  id: number;
  groupId: number;
  groupName: string;
  name: string;
  color: string;
}

export interface FinanceTransaction {
  id: number;
  type: TransactionType;
  amount: number;
  date: string;
  description: string;
  tags: TransactionTag[];
}

export interface TagTotal {
  tagId: number;
  tagName: string;
  groupName: string;
  color: string;
  income: number;
  expense: number;
  balance: number;
}

export interface FinanceSummary {
  from: string;
  to: string;
  income: number;
  expense: number;
  balance: number;
  tagTotals: TagTotal[];
}

export interface WeeklyForecastWeek {
  weekNumber: number;
  from: string;
  to: string;
  isCurrent: boolean;
  isEstimate: boolean;
  forecastIncome: number;
  forecastExpense: number;
  actualIncome: number;
  actualExpense: number;
  remainingBalance: number;
  tagTotals: TagTotal[];
}

export interface WeeklyForecast {
  monthFrom: string;
  monthTo: string;
  monthIncome: number;
  incomeSource: 'budget' | 'actual';
  remainingWeeklyBudget: number;
  remainingWeeksCount: number;
  weeks: WeeklyForecastWeek[];
}

export interface MonthlyFixedExpense {
  id: number;
  description: string;
  amount: number;
  week: number;
}

export interface CreateMonthlyFixedExpense {
  description: string;
  amount: number;
  week: number;
}

export interface CreateTransaction {
  type: TransactionType;
  amount: number;
  date: string;
  description: string;
  tagIds: number[];
}

export interface CreateTag {
  tagGroupId: number;
  name: string;
  color: string;
  parentTagId: number | null;
}

export interface BudgetItem {
  id: number;
  type: TransactionType;
  description: string;
  expectedAmount: number;
  tags: TransactionTag[];
}

export interface Budget {
  id: number;
  name: string;
  from: string;
  to: string;
  expectedIncome: number;
  expectedExpense: number;
  expectedBalance: number;
  items: BudgetItem[];
  comparisons: BudgetComparison[];
}

export interface BudgetComparison {
  key: string;
  typeName: string;
  subtypeName: string;
  expectedExpense: number;
  actualExpense: number;
  difference: number;
  usagePercentage: number;
  isExceeded: boolean;
}

export interface CreateBudget {
  name: string;
  from: string;
  to: string;
}

export interface CreateBudgetItem {
  type: TransactionType;
  description: string;
  expectedAmount: number;
  tagIds: number[];
}

export interface FixedExpense {
  id: number;
  type: TransactionType;
  description: string;
  amount: number;
  month: number;
  forecastWeek: number | null;
  tags: TransactionTag[];
}

export interface CreateFixedExpense {
  type: TransactionType;
  description: string;
  amount: number;
  month: number;
  tagIds: number[];
}

export interface SaveFixedExpenseForecastWeek {
  week: number | null;
}

export type SavingsMovementType = 'deposit' | 'withdrawal';

export interface SavingsAccount {
  id: number;
  name: string;
  color: string;
  balance: number;
  totalDeposits: number;
  totalWithdrawals: number;
}

export interface CreateSavingsAccount {
  name: string;
  color: string;
}

export type SavingsMovementSource = 'manual' | 'transaction';

export interface SavingsMovement {
  id: number;
  savingsAccountId: number;
  savingsAccountName: string;
  type: SavingsMovementType;
  amount: number;
  date: string;
  description: string;
  source: SavingsMovementSource;
}

export interface CreateSavingsMovement {
  savingsAccountId: number;
  type: SavingsMovementType;
  amount: number;
  date: string;
  description: string;
}

export interface SaveTagSavingsLink {
  savingsAccountId: number | null;
}

export interface BackupResult {
  fileName: string;
  relativePath: string;
  sizeBytes: number;
  createdAt: string;
}
