import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PersonsService, type CommandAccepted, type PersonRequest } from './persons.service';

describe('PersonsService', () => {
  let service: PersonsService;
  let http: HttpTestingController;
  const url = '/api/persons';
  const person: PersonRequest = { firstName: 'Ada', lastName: 'Lovelace', email: null };
  const accepted: CommandAccepted = {
    personId: 'person-id', commandId: 'command-id', action: 'Created', status: 'Accepted'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(PersonsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('gets the person list and a person by id', () => {
    const results: string[] = [];
    service.getPersons().subscribe(people => results.push(people[0].firstName ?? ''));
    service.getPerson('person-id').subscribe(found => results.push(found.lastName ?? ''));

    const listRequest = http.expectOne(url);
    expect(listRequest.request.method).toBe('GET');
    listRequest.flush([{ firstName: 'Ada' }]);
    const personRequest = http.expectOne(`${url}/person-id`);
    expect(personRequest.request.method).toBe('GET');
    personRequest.flush({ lastName: 'Lovelace' });
    expect(results).toEqual(['Ada', 'Lovelace']);
  });

  it('sends create, update, and delete commands and returns their receipts', () => {
    const receipts: CommandAccepted[] = [];
    service.createPerson(person).subscribe(result => receipts.push(result));
    service.updatePerson('person-id', person).subscribe(result => receipts.push(result));
    service.deletePerson('person-id').subscribe(result => receipts.push(result));

    const create = http.expectOne(url);
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual(person);
    create.flush(accepted, { status: 202, statusText: 'Accepted' });

    const update = http.expectOne(request => request.url === `${url}/person-id` && request.method === 'PUT');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual(person);
    update.flush(accepted, { status: 202, statusText: 'Accepted' });

    const remove = http.expectOne(request => request.url === `${url}/person-id` && request.method === 'DELETE');
    expect(remove.request.method).toBe('DELETE');
    remove.flush(accepted, { status: 202, statusText: 'Accepted' });
    expect(receipts).toEqual([accepted, accepted, accepted]);
  });
});