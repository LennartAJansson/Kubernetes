import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { App } from './app';
import { type CommandAccepted, type PersonDto, type PersonRequest, PersonsService } from './persons.service';

describe('App', () => {
  const persons: PersonDto[] = [
    { id: 'ada-id', firstName: 'Ada', lastName: 'Lovelace', email: 'ada@example.com' },
    { id: 'grace-id', firstName: 'Grace', lastName: 'Hopper', email: 'grace@example.com' }
  ];
  const accepted: CommandAccepted = { personId: 'ada-id', commandId: 'cmd-id', action: 'Updated', status: 'Accepted' };
  let calls: { action: string; id?: string; person?: PersonRequest }[];

  beforeEach(async () => {
    calls = [];
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [{
        provide: PersonsService,
        useValue: {
          getPersons: () => of(persons),
          createPerson: (person: PersonRequest) => {
            calls.push({ action: 'create', person });
            return of(accepted);
          },
          updatePerson: (id: string, person: PersonRequest) => {
            calls.push({ action: 'update', id, person });
            return of(accepted);
          },
          deletePerson: (id: string) => {
            calls.push({ action: 'delete', id });
            return of(accepted);
          }
        }
      }]
    }).compileComponents();
  });

  it('lists persons and filters by name or email', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent).toBe('Personer');
    expect(element.querySelectorAll('tbody tr').length).toBe(2);

    const search = element.querySelector<HTMLInputElement>('#person-search')!;
    search.value = 'grace@example.com';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(element.querySelectorAll('tbody tr').length).toBe(1);
    expect(element.querySelector('tbody')?.textContent).toContain('Grace Hopper');
  });

  it('creates a person from the form', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('.page-heading button')!.click();
    fixture.detectChanges();

    const firstName = element.querySelector<HTMLInputElement>('#first-name')!;
    firstName.value = 'Lin';
    firstName.dispatchEvent(new Event('input'));
    const lastName = element.querySelector<HTMLInputElement>('#last-name')!;
    lastName.value = 'Chen';
    lastName.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(calls).toEqual([{ action: 'create', person: { firstName: 'Lin', lastName: 'Chen', email: null } }]);
    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(element.querySelector('[role="status"]')?.textContent).toContain('tagits emot');
  });

  it('updates an existing person and confirms deletion', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('[aria-label="Redigera Ada Lovelace"]')!.click();
    fixture.detectChanges();
    const lastName = element.querySelector<HTMLInputElement>('#last-name')!;
    lastName.value = 'Byron';
    lastName.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(calls[0]).toEqual({ action: 'update', id: 'ada-id', person: { firstName: 'Ada', lastName: 'Byron', email: 'ada@example.com' } });

    element.querySelector<HTMLButtonElement>('[aria-label="Radera Ada Lovelace"]')!.click();
    fixture.detectChanges();
    expect(element.querySelector('[role="alertdialog"]')?.textContent).toContain('Ada Lovelace');
    element.querySelector<HTMLButtonElement>('.button-delete')!.click();
    fixture.detectChanges();
    expect(calls[1]).toEqual({ action: 'delete', id: 'ada-id' });
    expect(element.querySelector('[role="alertdialog"]')).toBeNull();
  });
});