import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PersonDto, PersonRequest, PersonsService } from './persons.service';

@Component({
  imports: [FormsModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly personsService = inject(PersonsService);
  protected readonly persons = signal<PersonDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly search = signal('');
  protected readonly filteredPersons = computed(() => {
    const query = this.search().trim().toLocaleLowerCase();
    return this.persons().filter(person =>
      `${person.firstName ?? ''} ${person.lastName ?? ''} ${person.email ?? ''}`
        .toLocaleLowerCase().includes(query)
    );
  });

  protected formOpen = false;
  protected editingId: string | null = null;
  protected deleteTarget: PersonDto | null = null;
  protected form: PersonRequest = { firstName: '', lastName: '', email: '' };

  ngOnInit(): void {
    this.loadPersons();
  }

  protected loadPersons(): void {
    this.loading.set(true);
    this.error.set('');
    this.personsService.getPersons().subscribe({
      next: persons => {
        this.persons.set(persons);
        this.loading.set(false);
      },
      error: error => {
        this.error.set(this.errorMessage(error));
        this.loading.set(false);
      }
    });
  }

  protected openCreate(): void {
    this.editingId = null;
    this.form = { firstName: '', lastName: '', email: '' };
    this.formOpen = true;
    this.error.set('');
  }

  protected openEdit(person: PersonDto): void {
    if (!person.id) return;
    this.editingId = person.id;
    this.form = {
      firstName: person.firstName ?? '',
      lastName: person.lastName ?? '',
      email: person.email ?? ''
    };
    this.formOpen = true;
    this.error.set('');
  }

  protected closeForm(): void {
    if (!this.saving()) this.formOpen = false;
  }

  protected savePerson(): void {
    const person: PersonRequest = {
      firstName: this.form.firstName?.trim() ?? '',
      lastName: this.form.lastName?.trim() ?? '',
      email: this.form.email?.trim() || null
    };
    if (!person.firstName || !person.lastName || this.saving()) return;

    this.saving.set(true);
    this.error.set('');
    const request = this.editingId
      ? this.personsService.updatePerson(this.editingId, person)
      : this.personsService.createPerson(person);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.formOpen = false;
        this.notice.set('Kommandot har tagits emot. Uppdatera listan om ändringen inte syns ännu.');
        this.loadPersons();
      },
      error: error => {
        this.error.set(this.errorMessage(error));
        this.saving.set(false);
      }
    });
  }

  protected confirmDelete(person: PersonDto): void {
    if (person.id) {
      this.error.set('');
      this.deleteTarget = person;
    }
  }

  protected deletePerson(): void {
    if (!this.deleteTarget?.id || this.saving()) return;
    this.saving.set(true);
    this.error.set('');
    this.personsService.deletePerson(this.deleteTarget.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleteTarget = null;
        this.notice.set('Raderingen har tagits emot. Uppdatera listan om personen fortfarande visas.');
        this.loadPersons();
      },
      error: error => {
        this.error.set(this.errorMessage(error));
        this.saving.set(false);
      }
    });
  }

  protected fullName(person: PersonDto): string {
    return [person.firstName, person.lastName].filter(Boolean).join(' ') || 'Namnlös person';
  }

  protected initials(person: PersonDto): string {
    return [person.firstName, person.lastName]
      .filter(Boolean).map(part => part?.charAt(0) ?? '').join('').toLocaleUpperCase() || '?';
  }

  protected createdDate(value?: string): string {
    if (!value) return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat('sv-SE', { dateStyle: 'medium' }).format(date);
  }

  private errorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const details = error.error;
      if (details?.errors && typeof details.errors === 'object') {
        const messages = Object.values(details.errors).flat();
        if (messages.length) return messages.join(' ');
      }
      if (typeof details?.detail === 'string') return details.detail;
      if (error.status === 0) return 'Kan inte nå API:t. Kontrollera att det är igång.';
    }
    return 'Något gick fel. Försök igen.';
  }
}
