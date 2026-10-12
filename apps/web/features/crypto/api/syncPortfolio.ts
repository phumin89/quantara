import readJson from '../../../shared/api/readJson';
import errorMessage from '../../../shared/api/errorMessage';

export async function syncPortfolio(signal?: AbortSignal): Promise<void> {
  const response = await fetch('/api/crypto/sync', {
    method: 'POST',
    cache: 'no-store',
    ...(signal ? { signal } : {}),
  });
  if (!response.ok) {
    throw new Error(
      errorMessage(await readJson(response), 'Unable to sync Bitkub data.'),
    );
  }
}
