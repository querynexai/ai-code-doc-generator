import { Component, OnInit, signal, inject, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProjectService, ProjectSummary } from '../project.service';

@Component({
  selector: 'app-projects',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './projects.html',
  styleUrls: ['./projects.css']
})
export class ProjectsComponent implements OnInit {
  private readonly api = inject(ProjectService);

  readonly projects = signal<ProjectSummary[]>([]);
  readonly loading = signal(true);
  readonly analyzing = signal(false);
  readonly message = signal('');
  readonly isError = signal(false);

  projectPath = '';
  generateReadme = true;
  generateXmlDocs = true;
  maxMembers = 30;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (data) => {
        this.projects.set(data);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  analyze(): void {
    if (!this.projectPath.trim()) return;

    this.analyzing.set(true);
    this.message.set('');
    this.isError.set(false);

    this.api.analyze(
      this.projectPath,
      this.generateReadme,
      this.generateXmlDocs,
      this.maxMembers
    ).subscribe({
      next: (res) => {
        this.analyzing.set(false);
        if (res.success) {
          this.message.set(
            `✅ Analyzed ${res.totalMembers} members. Generated ${res.generatedCount} docs, skipped ${res.skippedCount}.`
          );
          this.projectPath = '';
          this.load();
        } else {
          this.isError.set(true);
          this.message.set(`❌ ${res.error}`);
        }
      },
      error: (err) => {
        this.analyzing.set(false);
        this.isError.set(true);
        this.message.set(err.error?.error ?? 'Analysis failed.');
      }
    });
  }

  progressPercent(p: ProjectSummary): number {
    const total = p.documentedMembers + p.undocumentedMembers;
    return total === 0 ? 0 : Math.round((p.documentedMembers / total) * 100);
  }
}