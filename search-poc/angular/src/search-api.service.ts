import { HttpClient } from "@angular/common/http";
import { Injectable, inject } from "@angular/core";
import { Observable } from "rxjs";

export interface SearchResult {
  customer: string | null;
  status: string | null;
  amount: number | null;
  createdDate: string | null;
}

export interface SearchResponse {
  conditions: unknown[];
  results: SearchResult[];
}

@Injectable({ providedIn: "root" })
export class SearchApiService {
  private readonly http = inject(HttpClient);

  search(originalQuery: string): Observable<SearchResponse> {
    return this.http.post<SearchResponse>("/api/search", { query: originalQuery });
  }
}
