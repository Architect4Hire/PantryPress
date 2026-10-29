import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PpToastRegion } from '@pantrypress/ui';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpToastRegion, RouterOutlet],
  selector: 'ppa-root',
  templateUrl: './app.html',
})
export class App {}
