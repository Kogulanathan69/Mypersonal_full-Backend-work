import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { BuildMindTitleStrategy } from './title-strategy';

@Component({ template: '' })
class Blank {}

describe('BuildMindTitleStrategy', () => {
  it('appends the product name to page titles and has a default', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'a', title: 'Dashboard', component: Blank },
          { path: 'b', component: Blank },
        ]),
        { provide: TitleStrategy, useClass: BuildMindTitleStrategy },
      ],
    });
    const router = TestBed.inject(Router);
    const title = TestBed.inject(Title);
    await router.navigateByUrl('/a');
    expect(title.getTitle()).toBe('Dashboard · BuildMind');
    await router.navigateByUrl('/b');
    expect(title.getTitle()).toBe('BuildMind — AI Home Planning for Sri Lanka');
  });
});
