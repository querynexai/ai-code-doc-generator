import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface ProjectSummary {
  projectId: number;
  projectName: string;
  assemblyName: string | null;
  targetFramework: string | null;
  totalFiles: number;
  totalClasses: number;
  totalMethods: number;
  totalProperties: number;
  documentedMembers: number;
  undocumentedMembers: number;
  analysisStatus: string;
  analysisDurationMs: number | null;
  analyzedAt: string;
}

export interface GeneratedDoc {
  docId: number;
  memberType: string;
  memberName: string;
  fullyQualifiedName: string | null;
  filePath: string | null;
  lineNumber: number | null;
  originalXml: string | null;
  generatedXml: string | null;
  aiConfidence: number | null;
  wasAccepted: boolean;
}

export interface ProjectDetail extends ProjectSummary {
  docs: GeneratedDoc[];
  readmeContent: string | null;
}

export interface AnalysisResult {
  projectId: number;
  success: boolean;
  totalMembers: number;
  documentedMembers: number;
  undocumentedMembers: number;
  generatedCount: number;
  skippedCount: number;
  error: string | null;
}

export interface RegenerateReadmeResponse {
  success: boolean;
  length: number;
  readme?: string | null;
  error?: string | null;
}

@Injectable({ providedIn: 'root' })
export class ProjectService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = `${environment.apiBase}/projects`;

  list(): Observable<ProjectSummary[]> {
    return this.http.get<ProjectSummary[]>(this.apiBase);
  }

  get(id: number): Observable<ProjectDetail> {
    return this.http.get<ProjectDetail>(`${this.apiBase}/${id}`);
  }

  analyze(
    projectPath: string,
    generateReadme: boolean,
    generateXmlDocs: boolean,
    maxMembers: number = 50
  ): Observable<AnalysisResult> {
    return this.http.post<AnalysisResult>(`${this.apiBase}/analyze`, {
      projectPath,
      generateReadme,
      generateXmlDocs,
      maxMembers
    });
  }

  regenerateReadme(id: number): Observable<RegenerateReadmeResponse> {
    return this.http.post<RegenerateReadmeResponse>(`${this.apiBase}/${id}/readme`, {});
  }
}