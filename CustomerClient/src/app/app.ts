import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CustomerDto, CustomerRequest, CustomersService } from './customers.service';
import { trackEvent } from './faro';

@Component({
  imports: [FormsModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly customersService = inject(CustomersService);
  protected readonly customers = signal<CustomerDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly search = signal('');
  protected readonly filteredCustomers = computed(() => {
    const query = this.search().trim().toLocaleLowerCase();
    return this.customers().filter(c =>
      `${c.name} ${c.email ?? ''} ${c.city ?? ''}`.toLocaleLowerCase().includes(query)
    );
  });

  protected formOpen = false;
  protected editingId: string | null = null;
  protected deleteTarget: CustomerDto | null = null;
  protected form: CustomerRequest = this.emptyForm();

  ngOnInit(): void {
    this.loadCustomers();
  }

  protected loadCustomers(): void {
    this.loading.set(true);
    this.error.set('');
    this.customersService.getCustomers().subscribe({
      next: customers => {
        this.customers.set(customers);
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
    this.form = this.emptyForm();
    this.formOpen = true;
    this.error.set('');
  }

  protected openEdit(customer: CustomerDto): void {
    this.editingId = customer.id;
    this.form = {
      name: customer.name,
      email: customer.email ?? '',
      phone: customer.phone ?? '',
      city: customer.city ?? ''
    };
    this.formOpen = true;
    this.error.set('');
  }

  protected closeForm(): void {
    if (!this.saving()) this.formOpen = false;
  }

  protected saveCustomer(): void {
    const customer: CustomerRequest = {
      name: this.form.name?.trim() ?? '',
      email: this.form.email?.trim() || null,
      phone: this.form.phone?.trim() || null,
      city: this.form.city?.trim() || null
    };
    if (!customer.name || this.saving()) return;

    this.saving.set(true);
    this.error.set('');
    const editing = this.editingId;
    const request = editing
      ? this.customersService.updateCustomer(editing, customer)
      : this.customersService.createCustomer(customer);
    request.subscribe({
      next: saved => {
        this.saving.set(false);
        this.formOpen = false;
        trackEvent(editing ? 'customer_updated' : 'customer_created', { customerId: saved.id });
        this.notice.set(editing ? `${saved.name} har uppdaterats.` : `${saved.name} har lagts till.`);
        this.loadCustomers();
      },
      error: error => {
        this.error.set(this.errorMessage(error));
        this.saving.set(false);
      }
    });
  }

  protected confirmDelete(customer: CustomerDto): void {
    this.error.set('');
    this.deleteTarget = customer;
  }

  protected deleteCustomer(): void {
    if (!this.deleteTarget || this.saving()) return;
    const target = this.deleteTarget;
    this.saving.set(true);
    this.error.set('');
    this.customersService.deleteCustomer(target.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleteTarget = null;
        trackEvent('customer_deleted', { customerId: target.id });
        this.notice.set(`${target.name} har raderats.`);
        this.loadCustomers();
      },
      error: error => {
        this.error.set(this.errorMessage(error));
        this.saving.set(false);
      }
    });
  }

  // ---- Demo: ger något att titta på i Grafana -------------------------------
  protected runSlow(): void {
    this.notice.set('Skickar ett långsamt anrop (2 s)...');
    this.customersService.slow(2000).subscribe({
      next: () => this.notice.set('Det långsamma anropet är klart - leta efter det i Tempo.'),
      error: error => this.error.set(this.errorMessage(error))
    });
  }

  protected runError(): void {
    this.notice.set('');
    this.customersService.error().subscribe({
      next: () => {},
      error: () => this.error.set('API:t svarade 500 - som väntat. Leta efter det röda spannet i Tempo.')
    });
  }

  protected initials(customer: CustomerDto): string {
    return customer.name.split(' ').filter(Boolean).slice(0, 2)
      .map(part => part.charAt(0)).join('').toLocaleUpperCase() || '?';
  }

  protected createdDate(value?: string): string {
    if (!value) return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat('sv-SE', { dateStyle: 'medium' }).format(date);
  }

  private emptyForm(): CustomerRequest {
    return { name: '', email: '', phone: '', city: '' };
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
      if (error.status === 404) return 'Kunden finns inte längre.';
    }
    return 'Något gick fel. Försök igen.';
  }
}
