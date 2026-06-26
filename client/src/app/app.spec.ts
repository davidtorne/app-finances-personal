import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  let fixture: ComponentFixture<App>;
  let component: any;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(App);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function flushInitialData(groups: unknown[] = []): void {
    fixture.detectChanges();
    http.expectOne('/api/tag-groups').flush(groups);
    flushFinancialData('month', '2026-06-01', '2026-06-30', []);
  }

  function flushFinancialData(
    period: string,
    from: string,
    to: string,
    transactions: unknown[],
  ): void {
    http
      .expectOne(
        (request) =>
          request.url === '/api/summary' &&
          request.params.get('period') === period,
      )
      .flush({
        from,
        to,
        income: 100,
        expense: 25,
        balance: 75,
        tagTotals: [],
      });

    http
      .expectOne(
        (request) =>
          request.url === '/api/transactions/' &&
          request.params.get('from') === from &&
          request.params.get('to') === to,
      )
      .flush(transactions);
  }

  it('refreshes totals, movements and tags once on every period pointerdown', () => {
    flushInitialData();

    const weekButton = [...fixture.nativeElement.querySelectorAll('.segmented button')]
      .find((button: HTMLButtonElement) => button.textContent?.trim() === 'Setmana');
    weekButton.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));

    flushFinancialData('week', '2026-06-15', '2026-06-21', [
      {
        id: 1,
        type: 'expense',
        amount: 25,
        date: '2026-06-20',
        description: 'Moviment setmanal',
        tags: [],
      },
    ]);

    expect(component.period).toBe('week');
    expect(component.transactions).toHaveLength(1);
    expect(component.summary.balance).toBe(75);
  });

  it('reenables the transaction button and performs a full refresh after save', () => {
    flushInitialData();
    component.transactionForm = {
      type: 'expense',
      amount: 12.5,
      date: '2026-06-20',
      description: 'Prova guardat',
    };

    component.saveTransaction();
    expect(component.saving).toBe(true);
    http.expectOne('/api/transactions/').flush({ id: 10 });

    expect(component.saving).toBe(false);
    flushFinancialData('month', '2026-06-01', '2026-06-30', [
      {
        id: 10,
        type: 'expense',
        amount: 12.5,
        date: '2026-06-20',
        description: 'Prova guardat',
        tags: [],
      },
    ]);

    expect(component.transactions[0].description).toBe('Prova guardat');
    expect(component.notice).toBe('Moviment guardat correctament.');
  });

  it('reenables the tag button and refreshes groups and financial data', () => {
    flushInitialData();
    component.newTag = { tagGroupId: 2, name: 'Tag nou', color: '#2563eb' };

    component.createTag();
    expect(component.savingTag).toBe(true);
    http.expectOne('/api/tags').flush(41);

    expect(component.savingTag).toBe(false);
    http.expectOne('/api/tag-groups').flush([
      {
        id: 2,
        name: 'Tipus',
        selectionMode: 'single',
        isRequired: true,
        tags: [{ id: 41, name: 'Tag nou', color: '#2563eb', parentTagId: null }],
      },
    ]);
    flushFinancialData('month', '2026-06-01', '2026-06-30', []);

    expect(component.tagGroups[0].tags[0].name).toBe('Tag nou');
    expect(component.notice).toBe('Tag creat correctament.');
  });
});
