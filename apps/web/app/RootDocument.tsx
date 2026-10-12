import Providers from '../shared/theme/Providers';
import AppShell from '../shared/layout/AppShell';
import type { RootLayoutProps } from './RootLayoutProps';

export class RootDocument {
  // Next.js invokes this method as its server layout entry point.
  public static render(this: void, { children }: RootLayoutProps) {
    return (
      <html lang="en">
        <body>
          <Providers>
            <AppShell>{children}</AppShell>
          </Providers>
        </body>
      </html>
    );
  }
}
