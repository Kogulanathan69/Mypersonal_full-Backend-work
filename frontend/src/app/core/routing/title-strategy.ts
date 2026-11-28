import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

/** Every page title reads "<Page> · BuildMind", so screen-reader users hear where they are. */
@Injectable()
export class BuildMindTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(
      page ? `${page} · BuildMind` : 'BuildMind — AI Home Planning for Sri Lanka',
    );
  }
}
