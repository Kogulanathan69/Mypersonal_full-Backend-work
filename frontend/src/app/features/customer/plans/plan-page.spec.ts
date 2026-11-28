import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CustomerApi } from '../customer-api';
import { PlanPage } from './plan-page';

describe('PlanPage editing', () => {
  it('offers revision-aware resizing only after a room is selected', async () => {
    const plan = {
      planId:'pl1',projectId:'p1',revisionId:'r1',versionNumber:1,geometryHash:'g',landRevisionId:'l1',requirementsRevisionId:'q1',
      geometry:{schemaVersion:'1',projectId:'p1',planId:'pl1',landRevisionId:'l1',requirementsRevisionId:'q1',coordinateSystem:{type:'Local',unit:'m',origin:{x:0,y:0},xDirection:'East',yDirection:'North'},floors:[{id:'f1',level:0,rooms:[{id:'room1',name:'Bedroom',type:'Bedroom',polygon:[{x:0,y:0},{x:4,y:0},{x:4,y:3}],wallIds:[]}],walls:[],openings:[]}]},
      validation:{validationId:'v1',validatorVersion:'1',geometryHash:'g',geometryValid:true,requirementsMatch:true,regulatoryEvidenceStatus:'Unavailable',hasBlockingIssues:false,validatedAtUtc:'2026-11-28T00:00:00Z'},confirmation:null,createdAtUtc:'2026-11-28T00:00:00Z',changeSummary:null,notice:'Preliminary',
    };
    TestBed.configureTestingModule({imports:[PlanPage],providers:[provideRouter([]),{provide:CustomerApi,useValue:{getPlan:()=>of(plan),listPlanVersions:()=>of([])}}]});
    const fixture=TestBed.createComponent(PlanPage);fixture.componentRef.setInput('id','p1');fixture.componentRef.setInput('planId','pl1');fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Preview room resize');
    (fixture.nativeElement.querySelector('polygon') as SVGElement).dispatchEvent(new MouseEvent('click',{bubbles:true}));fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Preview room resize');
  });
});
