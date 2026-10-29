import { PpTone } from '@pantrypress/ui';
import { SiteStatus } from '../../core/sites/sites-client';

/** The pill's tone for a site's status; the words beside it carry the meaning, never the color alone. */
export function statusTone(status: SiteStatus): PpTone | 'neutral' {
  switch (status) {
    case 'live':
      return 'success';
    case 'provisioning':
      return 'info';
    case 'read-only':
      return 'warning';
    case 'suspended':
    case 'offline':
      return 'danger';
    default:
      return 'neutral';
  }
}
