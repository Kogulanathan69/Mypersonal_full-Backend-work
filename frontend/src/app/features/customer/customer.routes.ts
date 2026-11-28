import { Routes } from '@angular/router';

export const CUSTOMER_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    title: 'Dashboard',
    loadComponent: () => import('./projects/projects-page').then((m) => m.ProjectsPage),
  },
  {
    path: 'projects',
    title: 'Projects',
    loadComponent: () => import('./projects/projects-page').then((m) => m.ProjectsPage),
  },
  {
    path: 'start',
    title: 'Create project',
    loadComponent: () => import('./projects/create-project-page').then((m) => m.CreateProjectPage),
  },
  {
    path: 'projects/:id',
    title: 'Project',
    loadComponent: () => import('./projects/project-detail-page').then((m) => m.ProjectDetailPage),
  },
  {
    path: 'projects/:id/edit',
    title: 'Edit project',
    loadComponent: () => import('./projects/edit-project-page').then((m) => m.EditProjectPage),
  },
  {
    path: 'projects/:id/survey',
    title: 'Survey plan',
    loadComponent: () => import('./survey/survey-page').then((m) => m.SurveyPage),
  },
  {
    path: 'projects/:id/survey/:surveyId',
    title: 'Survey extraction evidence',
    loadComponent: () => import('./survey/survey-evidence-page').then((m) => m.SurveyEvidencePage),
  },
  { path: 'projects/:id/land', title: 'Land interpretation', loadComponent: () => import('./land/land-page').then((m) => m.LandPage) },
  { path: 'projects/:id/requirements', title: 'Home requirements', loadComponent: () => import('./requirements/requirements-page').then((m) => m.RequirementsPage) },
  { path: 'projects/:id/generation', title: 'Generate layouts', loadComponent: () => import('./generation/generation-page').then((m) => m.GenerationPage) },
  { path: 'projects/:id/plans/:planId', title: 'Planning layout', loadComponent: () => import('./plans/plan-page').then((m) => m.PlanPage) },
  { path: 'projects/:id/reports', title: 'Project reports', loadComponent: () => import('./reports/reports-page').then((m) => m.ReportsPage) },
  { path: 'projects/:id/review', title: 'Architect review', loadComponent: () => import('./review/review-page').then((m) => m.ReviewPage) },
  { path: 'projects/:id/regulations', title: 'Regulation evidence', loadComponent: () => import('./regulations/regulations-page').then((m) => m.RegulationsPage) },
  {
    path: 'profile',
    title: 'Profile and security',
    loadComponent: () => import('./profile/profile-page').then((m) => m.ProfilePage),
  },
];
