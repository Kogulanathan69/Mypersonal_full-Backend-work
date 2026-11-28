import { Pipe, PipeTransform } from '@angular/core';
import { AreaUnit, LengthUnit, formatArea, formatLength } from '../../core/units/units';

/** {{ room.widthMm | bmLength:'ft-in' }} → 12′ 10″ */
@Pipe({ name: 'bmLength' })
export class LengthPipe implements PipeTransform {
  transform(mm: number | null | undefined, unit: LengthUnit = 'm', decimals = 2): string {
    return mm == null ? '—' : formatLength(mm, unit, decimals);
  }
}

/** {{ site.areaM2 | bmArea:'perch' }} → 12.53 perches */
@Pipe({ name: 'bmArea' })
export class AreaPipe implements PipeTransform {
  transform(m2: number | null | undefined, unit: AreaUnit = 'm2', decimals = 2): string {
    return m2 == null ? '—' : formatArea(m2, unit, decimals);
  }
}
