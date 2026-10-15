import type { ReactNode } from "react";
import "./globals.css";

export const metadata = {
  title: {
    default: "AgentRouter",
    template: "%s | AgentRouter",
  },
  description: "A local model gateway for coding agents",
};

export default async function RootLayout({ children }: { children: ReactNode }) {
  const basePath = process.env.GITHUB_ACTIONS && process.env.GITHUB_REPOSITORY
    ? `/${process.env.GITHUB_REPOSITORY.split("/")[1]}`
    : "";

  return (
    <html lang="en" dir="ltr" suppressHydrationWarning>
      <body>
        <header className="site-nav">
          <a href={`${basePath}/`} className="agent-router-logo">
            <img src={`${basePath}/agent-router-mark.svg`} alt="AgentRouter" />
            <strong>AgentRouter</strong>
          </a>
          <nav>
            <a href={`${basePath}/guide/quick-start`}>Guide</a>
            <a href={`${basePath}/reference/cli`}>Reference</a>
            <a href={`${basePath}/integrations`}>Integrations</a>
            <a href="https://github.com/mehdihadeli/agent-router">GitHub</a>
          </nav>
        </header>
        <main className="site-shell">{children}</main>
        <footer className="site-footer">Route coding agents to the model that fits the task.</footer>
      </body>
    </html>
  );
}