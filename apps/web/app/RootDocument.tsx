import Providers from '../shared/theme/Providers';
import ServiceProvider from './providers/service-provider/ServiceProvider';
import { TeamActivityProvider } from '../features/team/components/team-activity-provider/TeamActivityProvider';
import { WorkspaceShell } from './providers/workspace-shell/WorkspaceShell';
import type { RootLayoutProps } from './RootLayoutProps';

export class RootDocument {
  // Next.js invokes this method as its server layout entry point.
  public static render(this: void, { children }: RootLayoutProps) {
    return (
      <html lang="en">
        <body>
          <Providers>
            <ServiceProvider>
              <TeamActivityProvider>
                <WorkspaceShell>{children}</WorkspaceShell>
              </TeamActivityProvider>
            </ServiceProvider>
          </Providers>
        </body>
      </html>
    );
  }
}
