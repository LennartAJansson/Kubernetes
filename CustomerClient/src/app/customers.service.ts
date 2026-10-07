import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface CustomerDto {
  id: string;
  name: string;
  email?: string | null;
  phone?: string | null;
  city?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CustomerRequest {
  name: string | null;
  email: string | null;
  phone: string | null;
  city: string | null;
}

@Injectable({ providedIn: 'root' })
export class CustomersService {
  private readonly http = inject(HttpClient);
  // Relativ adress: nginx (eller proxy.conf.json under ng serve) skickar
  // /api/... vidare till TelemetryApi.
  private readonly baseUrl = '/api/customers';

  getCustomers(search = ''): Observable<CustomerDto[]> {
    const params = search.trim() ? new HttpParams().set('search', search.trim()) : undefined;
    return this.http.get<CustomerDto[]>(this.baseUrl, { params });
  }

  getCustomer(id: string): Observable<CustomerDto> {
    return this.http.get<CustomerDto>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  createCustomer(customer: CustomerRequest): Observable<CustomerDto> {
    return this.http.post<CustomerDto>(this.baseUrl, customer);
  }

  updateCustomer(id: string, customer: CustomerRequest): Observable<CustomerDto> {
    return this.http.put<CustomerDto>(`${this.baseUrl}/${encodeURIComponent(id)}`, customer);
  }

  deleteCustomer(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  // Demo-anrop som ger något att titta på i Grafana.
  slow(ms = 2000): Observable<unknown> {
    return this.http.get(`/api/demo/slow`, { params: new HttpParams().set('ms', ms) });
  }

  error(): Observable<unknown> {
    return this.http.get(`/api/demo/error`);
  }
}
