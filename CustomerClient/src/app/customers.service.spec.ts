import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CustomersService, type CustomerDto, type CustomerRequest } from './customers.service';

describe('CustomersService', () => {
  let service: CustomersService;
  let http: HttpTestingController;
  const url = '/api/customers';
  const request: CustomerRequest = { name: 'Ada Lovelace', email: null, phone: null, city: 'Göteborg' };
  const ada: CustomerDto = { id: 'c1', name: 'Ada Lovelace', city: 'Göteborg', createdAt: '2026-10-08T18:00:00Z' };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(CustomersService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('hämtar listan och en kund', () => {
    const names: string[] = [];
    service.getCustomers().subscribe(list => names.push(list[0].name));
    service.getCustomer('c1').subscribe(c => names.push(c.city ?? ''));

    http.expectOne(url).flush([ada]);
    http.expectOne(`${url}/c1`).flush(ada);
    expect(names).toEqual(['Ada Lovelace', 'Göteborg']);
  });

  it('skickar search som query-parameter', () => {
    service.getCustomers(' göteborg ').subscribe();
    const req = http.expectOne(r => r.url === url);
    expect(req.request.params.get('search')).toBe('göteborg');
    req.flush([]);
  });

  it('skapar, uppdaterar och raderar', () => {
    service.createCustomer(request).subscribe();
    service.updateCustomer('c1', request).subscribe();
    service.deleteCustomer('c1').subscribe();

    const post = http.expectOne(r => r.method === 'POST');
    expect(post.request.url).toBe(url);
    expect(post.request.body).toEqual(request);
    post.flush(ada);

    const put = http.expectOne(r => r.method === 'PUT');
    expect(put.request.url).toBe(`${url}/c1`);
    put.flush(ada);

    const del = http.expectOne(r => r.method === 'DELETE');
    expect(del.request.url).toBe(`${url}/c1`);
    del.flush(null);
  });
});
