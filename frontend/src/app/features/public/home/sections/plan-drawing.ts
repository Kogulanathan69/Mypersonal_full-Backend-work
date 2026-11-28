import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { ROOMS, Room } from '../cinematic/house-model-data';

export type PlanVariant = 'a' | 'b' | 'c';

export function variantRooms(variant: PlanVariant): Room[] {
  if (variant === 'a') return [...ROOMS];
  if (variant === 'b') return ROOMS.map((room) => ({ ...room, x0: -room.x1, x1: -room.x0 }));
  return ROOMS.map((room) => ({ ...room, z0: -room.z1, z1: -room.z0 }));
}

/** Original, illustrative architectural plan used only in public marketing demonstrations. */
@Component({
  selector: 'bm-plan-drawing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <svg
      viewBox="0 0 720 470"
      class="bm-plan-drawing h-auto w-full"
      [attr.role]="selectable() ? 'group' : 'img'"
      [attr.aria-label]="label()"
      [style.--plan-zoom]="zoom()"
    >
      <defs>
        <pattern id="plan-grid" width="20" height="20" patternUnits="userSpaceOnUse">
          <path d="M20 0H0V20" class="bm-plan-grid" />
        </pattern>
        <g id="sleeping">
          <rect width="105" height="72" rx="3" />
          <rect x="6" y="6" width="42" height="18" rx="6" />
          <rect x="56" y="6" width="42" height="18" rx="6" />
          <path d="M5 30h95M52 30v38" />
        </g>
        <g id="sofa">
          <rect width="86" height="34" rx="8" />
          <path d="M8 7v20M78 7v20M31 3v28M57 3v28" />
        </g>
        <g id="dining">
          <ellipse cx="42" cy="28" rx="35" ry="23" />
          <circle cx="42" cy="0" r="8" />
          <circle cx="42" cy="56" r="8" />
          <circle cx="0" cy="28" r="8" />
          <circle cx="84" cy="28" r="8" />
        </g>
        <g id="bath">
          <rect width="34" height="54" rx="16" />
          <ellipse cx="17" cy="17" rx="10" ry="13" />
          <rect x="45" width="45" height="45" />
          <circle cx="68" cy="22" r="4" />
        </g>
        <g id="north">
          <path d="M0 34 12 0l12 34-12-7z" />
          <text x="12" y="48" text-anchor="middle">N</text>
        </g>
      </defs>
      <rect width="720" height="470" class="bm-plan-paper" />
      <rect width="720" height="470" fill="url(#plan-grid)" />
      <g
        class="bm-plan-content"
        [attr.transform]="'translate(360 235) scale(' + zoom() + ') translate(-360 -235)'"
      >
        <g class="bm-plan-annotation">
          <use href="#north" x="655" y="30" />
          <path d="M80 42h520M80 35v14M600 35v14" />
          <text x="340" y="34" text-anchor="middle">14.8 m</text>
          <path d="M46 75v330M39 75h14M39 405h14" />
          <text x="28" y="240" text-anchor="middle" transform="rotate(-90 28 240)">10.6 m</text>
          <text x="80" y="442">ILLUSTRATIVE PLAN · SCALE 1:100</text>
          <text x="600" y="442" text-anchor="end">NORTH ORIENTED</text>
        </g>

        @switch (variant()) {
          @case ('a') {
            <g class="bm-plan-walls">
              <path d="M80 75H600V405H80ZM92 87H588V393H92Z" fill-rule="evenodd" />
              <path d="M315 87v306M445 87v306M92 245h496M315 180h130M445 320h143" />
            </g>
            <g class="bm-plan-windows">
              <path d="M155 75h95M350 75h55M600 130v75M140 405h90M350 405h55" />
            </g>
            <g class="bm-plan-doors">
              <path d="M80 285h12a54 54 0 0 1 54 54" />
              <path d="M315 215h12a42 42 0 0 1 42 42" />
              <path d="M445 277h12a42 42 0 0 1 42 42" />
            </g>
            <g class="bm-plan-furniture">
              <use href="#sofa" x="145" y="135" />
              <rect x="185" y="205" width="62" height="28" rx="4" />
              <use href="#dining" x="333" y="112" />
              <path d="M325 225h110v14H325zM331 196h18v35M411 196h18v35" />
              <use href="#sleeping" x="470" y="112" />
              <use href="#sleeping" x="470" y="292" transform="scale(.82)" />
              <use href="#bath" x="335" y="274" />
              <rect x="103" y="340" width="70" height="22" />
            </g>
            <g class="bm-plan-labels">
              <text x="200" y="112">LIVING</text>
              <text x="380" y="112">DINING + KITCHEN</text>
              <text x="520" y="112">BEDROOM 01</text>
              <text x="200" y="278">VERANDAH / ENTRY</text>
              <text x="380" y="278">BATH + STORE</text>
              <text x="520" y="278">BEDROOM 02</text>
            </g>
          }
          @case ('b') {
            <g class="bm-plan-walls">
              <path d="M80 75H600V405H80ZM92 87H588V393H92Z" fill-rule="evenodd" />
              <path d="M270 87v306M430 87v306M92 225h178M430 255h158M270 305h160" />
            </g>
            <g class="bm-plan-windows">
              <path d="M125 75h90M315 75h70M490 75h65M600 285v70M145 405h75" />
            </g>
            <g class="bm-plan-doors">
              <path d="M320 405v-12a52 52 0 0 1 52-52" />
              <path d="M270 255h12a42 42 0 0 1 42 42" />
              <path d="M430 150h12a42 42 0 0 1 42 42" />
            </g>
            <g class="bm-plan-furniture">
              <use href="#sleeping" x="120" y="115" />
              <use href="#sleeping" x="465" y="115" />
              <use href="#sofa" x="292" y="230" />
              <rect x="315" y="278" width="66" height="28" rx="4" />
              <use href="#dining" x="474" y="300" transform="scale(.85)" />
              <path d="M280 100h140v16H280zM290 122h18v72M395 122h18v72" />
              <use href="#bath" x="115" y="285" />
            </g>
            <g class="bm-plan-labels">
              <text x="180" y="108">BEDROOM 01</text>
              <text x="350" y="108">KITCHEN</text>
              <text x="510" y="108">BEDROOM 02</text>
              <text x="175" y="258">BATH + LAUNDRY</text>
              <text x="350" y="335">LIVING / ENTRY</text>
              <text x="510" y="285">DINING</text>
            </g>
          }
          @case ('c') {
            <g class="bm-plan-walls">
              <path d="M80 75H600V405H80ZM92 87H588V393H92Z" fill-rule="evenodd" />
              <path d="M92 205h496M355 87v318M475 205v188M92 315h263M475 300h113" />
            </g>
            <g class="bm-plan-windows">
              <path d="M135 75h85M400 75h50M600 110v65M600 325v45M150 405h80" />
            </g>
            <g class="bm-plan-doors">
              <path d="M285 405v-12a55 55 0 0 1 55-55" />
              <path d="M355 235h12a42 42 0 0 1 42 42" />
              <path d="M475 330h12a38 38 0 0 1 38 38" />
            </g>
            <g class="bm-plan-furniture">
              <use href="#sofa" x="145" y="120" />
              <rect x="175" y="167" width="65" height="25" rx="4" />
              <use href="#dining" x="390" y="115" transform="scale(.82)" />
              <path d="M485 95h90v15H485zM495 116h18v68M548 116h18v68" />
              <use href="#sleeping" x="115" y="230" />
              <use href="#sleeping" x="490" y="315" transform="scale(.72)" />
              <use href="#bath" x="385" y="235" />
            </g>
            <g class="bm-plan-labels">
              <text x="220" y="108">LIVING</text>
              <text x="405" y="108">DINING</text>
              <text x="525" y="108">KITCHEN</text>
              <text x="220" y="230">BEDROOM 01</text>
              <text x="415" y="230">BATH + STORE</text>
              <text x="525" y="230">BEDROOM 02</text>
              <text x="220" y="345">ENTRY COURT</text>
            </g>
          }
        }

        @if (selectable()) {
          @for (room of rooms(); track $index; let i = $index) {
            <g
              role="button"
              tabindex="0"
              [attr.aria-label]="'Select ' + room.name"
              [attr.aria-pressed]="selected() === i"
              (click)="pick(i)"
              (keydown.enter)="pick(i)"
              (keydown.space)="$event.preventDefault(); pick(i)"
              class="bm-plan-room-hit"
            >
              <rect
                [attr.x]="roomHit(i).x"
                [attr.y]="roomHit(i).y"
                [attr.width]="roomHit(i).w"
                [attr.height]="roomHit(i).h"
                [class.is-selected]="selected() === i"
              />
            </g>
          }
        }
      </g>
    </svg>
  `,
})
export class PlanDrawing {
  readonly variant = input<PlanVariant>('a');
  readonly selectable = input(false);
  readonly showLabels = input(true);
  readonly showDimensions = input(false);
  readonly selected = input<number | null>(null);
  readonly zoom = input(1);
  readonly label = input('Illustrative furnished 2D floor plan');
  readonly selectedChange = output<number>();

  protected readonly rooms = computed(() => variantRooms(this.variant()));
  protected readonly roomHit = (index: number) => {
    const hits = [
      { x: 92, y: 87, w: 223, h: 158 },
      { x: 315, y: 87, w: 130, h: 158 },
      { x: 445, y: 87, w: 143, h: 158 },
      { x: 315, y: 245, w: 130, h: 74 },
      { x: 445, y: 245, w: 143, h: 148 },
      { x: 315, y: 319, w: 130, h: 74 },
      { x: 92, y: 245, w: 223, h: 148 },
    ];
    return hits[index % hits.length]!;
  };

  protected pick(index: number): void {
    if (this.selectable()) this.selectedChange.emit(index);
  }
}
