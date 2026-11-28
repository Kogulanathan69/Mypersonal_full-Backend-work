import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { PERMISSION_CHECKER, Permission } from '../../core/auth/permission';

/**
 * Renders its content only when the current user has the permission:
 *   <button *bmHasPermission="'admin.users.suspend'">Suspend</button>
 * This only hides UI. The backend must still reject unauthorised requests.
 * Until step 7 wires the session, the default checker denies everything.
 */
@Directive({ selector: '[bmHasPermission]' })
export class HasPermission {
  readonly bmHasPermission = input.required<Permission>();
  private readonly checker = inject(PERMISSION_CHECKER);
  private readonly template = inject(TemplateRef);
  private readonly container = inject(ViewContainerRef);
  private shown = false;

  constructor() {
    effect(() => {
      const allowed = this.checker(this.bmHasPermission());
      if (allowed && !this.shown) {
        this.container.createEmbeddedView(this.template);
        this.shown = true;
      } else if (!allowed && this.shown) {
        this.container.clear();
        this.shown = false;
      }
    });
  }
}
