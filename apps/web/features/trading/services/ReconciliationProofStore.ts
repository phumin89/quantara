import {
  reconciliationPreviewSchema,
  type ReconciliationPreview,
} from '../contracts/ReconciliationPreview';

// Only an uncertain, owner-clicked confirmation is retained in this tab.
// No credential or checked acknowledgement is stored; recovery never sends a request.
export class ReconciliationProofStore {
  private static key(account: string): string {
    return `quantara-reconciliation-pending:${account}`;
  }

  public static read(account: string): ReconciliationPreview | null {
    const text = sessionStorage.getItem(this.key(account));
    if (!text) {
      return null;
    }

    if (text.length > 32000) {
      throw new Error(
        'Saved confirmation is invalid. Its previous outcome is unknown.',
      );
    }

    let value: unknown;
    try {
      value = JSON.parse(text) as unknown;
    } catch {
      throw new Error(
        'Saved confirmation cannot be read. Its previous outcome is unknown.',
      );
    }

    const parsed = reconciliationPreviewSchema.safeParse(value);
    if (!parsed.success || parsed.data.binding.accountKey !== account) {
      throw new Error(
        'Saved confirmation cannot be verified for this account. Its previous outcome is unknown.',
      );
    }

    return parsed.data;
  }

  public static save(preview: ReconciliationPreview): void {
    const parsed = reconciliationPreviewSchema.safeParse(preview);
    if (!parsed.success) {
      throw new Error(
        'Confirmation cannot be retained for recovery. Nothing was sent.',
      );
    }

    const pending = this.read(preview.binding.accountKey);
    if (pending && pending.result.proof !== preview.result.proof) {
      throw new Error(
        'A previous confirmation is unresolved. Review or explicitly discard it before another confirmation.',
      );
    }

    const text = JSON.stringify(parsed.data);
    if (text.length > 32000) {
      throw new Error(
        'Confirmation is too large to retain safely. Nothing was sent.',
      );
    }

    sessionStorage.setItem(this.key(preview.binding.accountKey), text);
    if (sessionStorage.getItem(this.key(preview.binding.accountKey)) !== text) {
      throw new Error(
        'Unable to retain this confirmation for recovery. Nothing was sent.',
      );
    }
  }

  public static clear(account: string, proof?: string): void {
    if (proof !== undefined && this.read(account)?.result.proof !== proof) {
      return;
    }

    sessionStorage.removeItem(this.key(account));
  }
}
