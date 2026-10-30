import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, forkJoin } from 'rxjs';
import { Batch, Inbox, LedgerRow, Run, SheetType, Workbook, WorkbooksApi } from './workbooks-api';

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit, OnDestroy {
  private readonly api = inject(WorkbooksApi);
  private timer?: ReturnType<typeof setInterval>;
  private refreshing = false;
  private generation = 0;
  clientInput = 'SYNTH-001';
  readonly client = signal('SYNTH-001');
  readonly workbooks = signal<Workbook[]>([]);
  readonly inbox = signal<Inbox>({ pending: [], rejected: [] });
  readonly selectedId = signal<string | null>(null);
  readonly type = signal<SheetType>('DPL');
  readonly types: SheetType[] = ['DPL', 'TFA', 'CRD'];
  readonly rows = signal<LedgerRow[]>([]);
  readonly runs = signal<Run[]>([]);
  readonly batches = signal<Batch[]>([]);
  readonly skip = signal(0);
  readonly total = signal(0);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  file: File | null = null;
  readonly selected = computed(() => this.workbooks().find((w) => w.id === this.selectedId()));
  readonly sheet = computed(() => this.selected()?.sheets.find((s) => s.type === this.type()));
  readonly currentRuns = computed(() => this.runs().filter((r) => r.sheetType === this.type()));
  readonly currentBatches = computed(() =>
    this.batches().filter((b) => b.sheetType === this.type()),
  );
  readonly canCategorize = computed(() => !!this.sheet()?.canCategorize && !this.busy());

  ngOnInit() {
    void this.refresh();
    this.timer = setInterval(() => void this.refresh(), 4000);
  }
  ngOnDestroy() {
    clearInterval(this.timer);
    this.generation++;
  }
  private failure(error: unknown) {
    this.error.set(
      error instanceof HttpErrorResponse
        ? error.error?.error || error.error?.detail || error.message
        : String(error),
    );
  }
  async changeClient() {
    const value = this.clientInput.trim().toUpperCase();
    if (!/^[A-Z0-9][A-Z0-9_-]{0,63}$/.test(value)) {
      this.error.set('Enter a valid client ID.');
      return;
    }
    this.generation++;
    this.client.set(value);
    this.selectedId.set(null);
    this.workbooks.set([]);
    this.rows.set([]);
    this.runs.set([]);
    this.batches.set([]);
    this.skip.set(0);
    this.total.set(0);
    this.error.set('');
    this.message.set('');
    await this.refresh();
  }
  async refresh() {
    if (this.refreshing) return;
    this.refreshing = true;
    const generation = this.generation;
    const client = this.client();
    try {
      const result = await firstValueFrom(
        forkJoin({ workbooks: this.api.list(client), inbox: this.api.inbox(client) }),
      );
      if (generation !== this.generation) return;
      this.workbooks.set(result.workbooks);
      this.inbox.set(result.inbox);
      this.error.set('');
      if (!this.selectedId() || !result.workbooks.some((w) => w.id === this.selectedId()))
        this.selectedId.set(result.workbooks[0]?.id ?? null);
      await this.loadRows();
    } catch (error) {
      if (generation === this.generation) this.failure(error);
    } finally {
      this.refreshing = false;
    }
  }
  async selectWorkbook(id: string) {
    this.generation++;
    this.selectedId.set(id);
    this.skip.set(0);
    await this.loadRows();
  }
  async selectType(type: SheetType) {
    this.generation++;
    this.type.set(type);
    this.skip.set(0);
    await this.loadRows();
  }
  async page(direction: number) {
    this.generation++;
    this.skip.update((skip) => Math.max(0, skip + direction * 20));
    await this.loadRows();
  }
  async loadRows() {
    const id = this.selectedId();
    if (!id) {
      this.rows.set([]);
      this.total.set(0);
      return;
    }
    const generation = this.generation;
    const client = this.client();
    const type = this.type();
    const skip = this.skip();
    try {
      const result = await firstValueFrom(
        forkJoin({
          page: this.api.records(client, id, type, skip),
          runs: this.api.runs(client, id),
          batches: this.api.batches(client, id),
        }),
      );
      if (generation !== this.generation) return;
      this.rows.set(result.page.rows);
      this.total.set(result.page.total);
      this.runs.set(result.runs);
      this.batches.set(result.batches);
    } catch (error) {
      if (generation === this.generation) this.failure(error);
    }
  }
  chooseFile(event: Event) {
    this.file = (event.target as HTMLInputElement).files?.[0] ?? null;
  }
  async upload() {
    if (!this.file) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.upload(this.client(), this.file));
      this.message.set(
        'File queued. The background job will import raw rows into SQL; prediction waits for Categorize.',
      );
      await this.refresh();
    } catch (error) {
      this.failure(error);
    } finally {
      this.busy.set(false);
    }
  }
  async categorize() {
    const id = this.selectedId();
    if (!id || !this.canCategorize()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.categorize(this.client(), id, this.type()));
      this.message.set(`${this.type()} categorization queued in batches of 20.`);
      await this.refresh();
    } catch (error) {
      this.failure(error);
    } finally {
      this.busy.set(false);
    }
  }
}
