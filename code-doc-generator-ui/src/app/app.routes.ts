import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'projects', pathMatch: 'full' },
  {
    path: 'projects',
    loadComponent: () => import('./projects/projects').then(m => m.ProjectsComponent)
  },
  {
    path: 'projects/:id',
    loadComponent: () => import('./project-detail/project-detail').then(m => m.ProjectDetailComponent)
  }
];