import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface PersonDto {
  id?: string;
  firstName?: string;
  lastName?: string;
  email?: string | null;
  createdAt?: string;
  updatedAt?: string | null;
}

export interface PersonRequest {
  firstName: string | null;
  lastName: string | null;
  email: string | null;
}

export interface CommandAccepted {
  personId: string;
  commandId: string;
  action: string;
  status: string;
}

export interface HttpValidationProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | string | null;
  detail?: string | null;
  instance?: string | null;
  errors?: Record<string, string[]>;
}

@Injectable({ providedIn: 'root' })
export class PersonsService {
  private readonly http = inject(HttpClient);
  // Relativ adress: anropet går till samma origin som appen laddades från.
  // I klustret skickar nginx /api/... vidare till CQRSApi och lägger på X-API-KEY.
  // Med ng serve gör proxy.conf.json samma sak.
  private readonly baseUrl = '/api/persons';

  getPersons(): Observable<PersonDto[]> {
    return this.http.get<PersonDto[]>(this.baseUrl);
  }

  getPerson(id: string): Observable<PersonDto> {
    return this.http.get<PersonDto>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  createPerson(person: PersonRequest): Observable<CommandAccepted> {
    return this.http.post<CommandAccepted>(this.baseUrl, person);
  }

  updatePerson(id: string, person: PersonRequest): Observable<CommandAccepted> {
    return this.http.put<CommandAccepted>(`${this.baseUrl}/${encodeURIComponent(id)}`, person);
  }

  deletePerson(id: string): Observable<CommandAccepted> {
    return this.http.delete<CommandAccepted>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}