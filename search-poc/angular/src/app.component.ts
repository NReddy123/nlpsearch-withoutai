import { CommonModule } from "@angular/common";
import { Component, OnDestroy, OnInit, inject } from "@angular/core";
import { Subject, Subscription, debounceTime, distinctUntilChanged } from "rxjs";
import registryJson from "../../shared/field-registry.json";
import type { FieldRegistry, QueryError, ValidatedQuery } from "../../typescript/src/index";
import { parseQuery, validateQuery } from "../../typescript/src/index";
import { SearchApiService, type SearchResponse } from "./search-api.service";

const registry = registryJson as FieldRegistry;

@Component({
  selector: "search-poc-root",
  standalone: true,
  imports: [CommonModule],
  templateUrl: "./app.component.html",
  styleUrl: "./app.component.css"
})
export class AppComponent implements OnInit, OnDestroy {
  private readonly api = inject(SearchApiService);
  private readonly changes = new Subject<string>();
  private readonly subscription = new Subscription();
  query = 'status IN ("Open", "Pending") AND amount RANGE (1000, 5000)';
  validationError: QueryError | null = null;
  preview: ValidatedQuery | null = null;
  response: SearchResponse | null = null;
  requestError: string | null = null;
  loading = false;

  ngOnInit(): void {
    this.subscription.add(this.changes.pipe(
      debounceTime(250),
      distinctUntilChanged()
    ).subscribe(query => this.validateLocally(query)));
    this.validateLocally(this.query);
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
    this.changes.complete();
  }

  onQueryInput(event: Event): void {
    this.query = (event.target as HTMLTextAreaElement).value;
    this.response = null;
    this.requestError = null;
    this.changes.next(this.query);
  }

  runSearch(): void {
    if (this.validationError || !this.preview || this.loading) return;
    this.loading = true;
    this.requestError = null;
    this.api.search(this.query).subscribe({
      next: response => {
        this.response = response;
        this.loading = false;
      },
      error: error => {
        const detail = error?.error as QueryError | undefined;
        this.requestError = detail?.message
          ? `${detail.message} (characters ${detail.start}–${detail.start + detail.length})`
          : "Search request failed.";
        this.loading = false;
      }
    });
  }

  private validateLocally(query: string): void {
    const parsed = parseQuery(query);
    if (!parsed.ok) {
      this.validationError = parsed.error;
      this.preview = null;
      return;
    }

    const validated = validateQuery(parsed.value, registry);
    if (!validated.ok) {
      this.validationError = validated.error;
      this.preview = null;
      return;
    }

    this.validationError = null;
    this.preview = validated.value;
  }
}
