import { Routes } from '@angular/router';
export const ARCHITECT_ROUTES:Routes=[
 {path:'',title:'Architect dashboard',loadComponent:()=>import('./dashboard-page').then(m=>m.ArchitectDashboardPage)},
 {path:'requests',title:'Architect requests',loadComponent:()=>import('./requests-page').then(m=>m.ArchitectRequestsPage)},
 {path:'requests/:requestId',title:'Review request',loadComponent:()=>import('./request-page').then(m=>m.ArchitectRequestPage)},
];
