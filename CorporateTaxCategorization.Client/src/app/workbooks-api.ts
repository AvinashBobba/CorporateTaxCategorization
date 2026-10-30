import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export type SheetType = 'DPL' | 'TFA' | 'CRD';
export interface SheetSummary {
  type: SheetType;
  total: number;
  pending: number;
  canCategorize: boolean;
}
export interface Workbook {
  id: string;
  entityId: string;
  period: string;
  total: number;
  pending: number;
  predicted: number;
  importStatus: string;
  sheets: SheetSummary[];
}
export interface Inbox {
  pending: string[];
  rejected: { file: string; error: string }[];
}
export interface Run {
  id: string;
  sheetType: SheetType;
  status: string;
  processed: number;
  error: string | null;
}
export interface Batch {
  id: string;
  sheetType: SheetType;
  processed: number;
}
export interface LedgerRow {
  id: number;
  recordId: string;
  sourceRow: number;
  description: string;
  amount: number;
  currency: string;
  status: string;
  suggestedCategory: string | null;
  similarity: number | null;
}
export interface RecordsPage {
  total: number;
  rows: LedgerRow[];
}

@Injectable({ providedIn: 'root' })
export class WorkbooksApi {
  private readonly http = inject(HttpClient);
  private base(client: string) {
    return `/api/clients/${encodeURIComponent(client)}/workbooks`;
  }
  list(client: string) {
    return this.http.get<Workbook[]>(this.base(client));
  }
  inbox(client: string) {
    return this.http.get<Inbox>(`${this.base(client)}/inbox`);
  }
  upload(client: string, file: File) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<{ uploadId: string }>(this.base(client), form);
  }
  categorize(client: string, id: string, type: SheetType) {
    return this.http.post<Run>(`${this.base(client)}/${id}/categorize?type=${type}`, {});
  }
  records(client: string, id: string, type: SheetType, skip: number) {
    return this.http.get<RecordsPage>(
      `${this.base(client)}/${id}/records?type=${type}&skip=${skip}&take=20`,
    );
  }
  runs(client: string, id: string) {
    return this.http.get<Run[]>(`${this.base(client)}/${id}/runs`);
  }
  batches(client: string, id: string) {
    return this.http.get<Batch[]>(`${this.base(client)}/${id}/batches`);
  }
}
