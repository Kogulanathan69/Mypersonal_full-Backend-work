import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { Icon } from '../icon/icon';

export interface RejectedFile {
  readonly name: string;
  readonly reason: 'type' | 'size' | 'count';
  readonly message: string;
}

/** Formats bytes for people: 20971520 → "20 MB". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let i = 0;
  while (value >= 1024 && i < units.length - 1) {
    value /= 1024;
    i++;
  }
  return `${Number.isInteger(value) ? value : value.toFixed(1)} ${units[i]}`;
}

/** Checks a file against accepted extensions/MIME types and a size limit. UX only: the server re-checks. */
export function checkFile(
  file: File,
  accept: readonly string[],
  maxBytes: number,
): RejectedFile | null {
  const name = file.name.toLowerCase();
  const typeOk =
    accept.length === 0 ||
    accept.some((a) =>
      a.startsWith('.')
        ? name.endsWith(a.toLowerCase())
        : a.endsWith('/*')
          ? file.type.startsWith(a.slice(0, -1))
          : file.type === a,
    );
  if (!typeOk) {
    return {
      name: file.name,
      reason: 'type',
      message: `${file.name}: this file type is not supported.`,
    };
  }
  if (file.size > maxBytes) {
    return {
      name: file.name,
      reason: 'size',
      message: `${file.name} is larger than ${formatBytes(maxBytes)}.`,
    };
  }
  return null;
}

/**
 * Drag-and-drop or click-to-choose file picker. Validates type and size in the browser for
 * fast feedback and emits accepted files; it never uploads by itself.
 */
@Component({
  selector: 'bm-file-drop',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div
      class="flex flex-col items-center gap-3 rounded-lg border-2 border-dashed px-6 py-8 text-center transition-colors"
      [class]="dragging() ? 'border-accent bg-surface-raised' : 'border-border-strong bg-surface'"
      (dragover)="onDragOver($event)"
      (dragleave)="dragging.set(false)"
      (drop)="onDrop($event)"
    >
      <bm-icon name="upload" [size]="28" class="text-accent" />
      <p class="text-sm text-text">Drag and drop {{ multiple() ? 'files' : 'a file' }} here, or</p>
      <button
        type="button"
        class="rounded-pill border border-accent px-4 py-2 text-sm font-medium text-accent hover:bg-accent hover:text-on-accent"
        [disabled]="disabled()"
        [attr.aria-describedby]="hintId"
        (click)="picker.click()"
      >
        {{ buttonLabel() }}
      </button>
      <p [id]="hintId" class="text-xs text-subtle">{{ hint() }}</p>
      <input
        #picker
        type="file"
        class="sr-only"
        tabindex="-1"
        aria-hidden="true"
        [accept]="accept().join(',')"
        [multiple]="multiple()"
        (change)="onPicked($event)"
      />
    </div>
    <div aria-live="polite">
      @if (rejected().length) {
        <ul class="mt-2 space-y-1 text-sm text-danger">
          @for (r of rejected(); track $index) {
            <li>{{ r.message }}</li>
          }
        </ul>
      }
    </div>
  `,
})
export class FileDrop {
  /** Extensions (".pdf") and/or MIME types ("image/png", "image/*"). */
  readonly accept = input<readonly string[]>([]);
  readonly maxBytes = input(20 * 1024 * 1024);
  readonly multiple = input(false);
  readonly maxFiles = input(5);
  readonly disabled = input(false);
  readonly buttonLabel = input('Choose file');

  readonly filesAccepted = output<File[]>();
  readonly filesRejected = output<RejectedFile[]>();

  protected readonly dragging = signal(false);
  protected readonly rejected = signal<readonly RejectedFile[]>([]);
  protected readonly hintId = `bm-file-hint-${Math.random().toString(36).slice(2, 8)}`;
  protected readonly hint = computed(() => {
    const types = this.accept()
      .map((a) => a.replace('.', '').toUpperCase())
      .join(', ');
    return `${types ? `Supported: ${types}. ` : ''}Max size: ${formatBytes(this.maxBytes())}${this.multiple() ? ` · up to ${this.maxFiles()} files` : ''}.`;
  });

  protected onDragOver(event: DragEvent): void {
    if (this.disabled()) return;
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (this.disabled()) return;
    this.handle(Array.from(event.dataTransfer?.files ?? []));
  }

  protected onPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.handle(Array.from(input.files ?? []));
    input.value = '';
  }

  /** Visible for tests. */
  handle(files: File[]): void {
    const limit = this.multiple() ? this.maxFiles() : 1;
    const accepted: File[] = [];
    const rejected: RejectedFile[] = [];
    for (const file of files) {
      const problem = checkFile(file, this.accept(), this.maxBytes());
      if (problem) rejected.push(problem);
      else if (accepted.length >= limit) {
        rejected.push({
          name: file.name,
          reason: 'count',
          message: `${file.name}: only ${limit} file(s) allowed.`,
        });
      } else accepted.push(file);
    }
    this.rejected.set(rejected);
    if (accepted.length) this.filesAccepted.emit(accepted);
    if (rejected.length) this.filesRejected.emit(rejected);
  }
}
