import { Component, OnInit, signal, computed, inject, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ProjectService, ProjectDetail, GeneratedDoc } from '../project.service';

@Component({
  selector: 'app-project-detail',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './project-detail.html',
  styleUrls: ['./project-detail.css']
})
export class ProjectDetailComponent implements OnInit {
  private readonly api = inject(ProjectService);
  private readonly route = inject(ActivatedRoute);

  readonly project = signal<ProjectDetail | null>(null);
  readonly filter = signal<string>('all');
  readonly loading = signal(true);
  readonly showReadme = signal(true);
  readonly copyMessage = signal('');
  readonly regenerating = signal(false);
  readonly regenError = signal('');

  readonly docsWithXml = computed(() => {
    const p = this.project();
    return p ? p.docs.filter(d => !!d.generatedXml).length : 0;
  });

  readonly docsWithOriginal = computed(() => {
    const p = this.project();
    return p ? p.docs.filter(d => !!d.originalXml).length : 0;
  });

  private projectId = 0;

  ngOnInit(): void {
    this.projectId = Number(this.route.snapshot.paramMap.get('id'));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get(this.projectId).subscribe({
      next: (p) => {
        this.project.set(p);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  regenerateReadme(): void {
    this.regenerating.set(true);
    this.regenError.set('');

    this.api.regenerateReadme(this.projectId).subscribe({
      next: (res) => {
        this.regenerating.set(false);
        if (res.success) {
          this.load();
        } else {
          this.regenError.set(res.error ?? 'Unknown error');
        }
      },
      error: (err) => {
        this.regenerating.set(false);
        this.regenError.set(err.error?.error ?? 'Request failed');
      }
    });
  }

  setFilter(f: string): void {
    this.filter.set(f);
  }

  filteredDocs(): GeneratedDoc[] {
    const p = this.project();
    if (!p) return [];
    if (this.filter() === 'all') return p.docs;
    return p.docs.filter(d => d.memberType === this.filter());
  }

  countByType(type: string): number {
    const p = this.project();
    return p ? p.docs.filter(d => d.memberType === type).length : 0;
  }

  memberTypes(): string[] {
    const p = this.project();
    if (!p) return [];
    return [...new Set(p.docs.map(d => d.memberType))].sort();
  }

  confidenceClass(c: number | null): string {
    if (c === null) return '';
    if (c >= 0.9) return 'high';
    if (c >= 0.75) return 'medium';
    return 'low';
  }

  downloadReadme(): void {
    const p = this.project();
    if (!p?.readmeContent) return;
    const blob = new Blob([p.readmeContent], { type: 'text/markdown' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${p.projectName}-README.md`;
    a.click();
    URL.revokeObjectURL(url);
  }

  downloadAllXml(): void {
    const p = this.project();
    if (!p) return;
    const lines: string[] = [
      `// AI-Generated XML Documentation`,
      `// Project: ${p.projectName}`,
      `// Generated: ${new Date().toISOString()}`,
      ''
    ];
    for (const d of p.docs) {
      if (d.generatedXml) {
        lines.push(`// ${d.memberType}: ${d.fullyQualifiedName ?? d.memberName}`);
        if (d.filePath) lines.push(`// File: ${d.filePath}:${d.lineNumber ?? '?'}`);
        lines.push(d.generatedXml);
        lines.push('');
      }
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${p.projectName}-xml-docs.txt`;
    a.click();
    URL.revokeObjectURL(url);
  }

  copyToClipboard(text: string): void {
    navigator.clipboard.writeText(text).then(() => {
      this.copyMessage.set('Copied!');
      setTimeout(() => this.copyMessage.set(''), 1500);
    });
  }
}