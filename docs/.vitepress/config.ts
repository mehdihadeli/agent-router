import { defineConfig } from "vitepress";

const repositoryName = process.env.GITHUB_REPOSITORY?.split("/")[1];

export default defineConfig({
  title: "AgentRouter",
  description: "A local model gateway for coding agents",
  base: process.env.GITHUB_ACTIONS && repositoryName ? `/${repositoryName}/` : "/",
  cleanUrls: true,
  appearance: true,
  lastUpdated: true,
  themeConfig: {
    logo: { src: "/agent-router-mark.svg", alt: "AgentRouter" },
    nav: [
      { text: "Home", link: "/" },
      { text: "Guide", link: "/guide/quick-start" },
      { text: "Reference", link: "/reference/cli" },
      { text: "Integrations", link: "/integrations" },
    ],
    sidebar: {
      "/guide/": [
        {
          text: "Get started",
          items: [
            { text: "Quick start", link: "/guide/quick-start" },
            { text: "Providers", link: "/guide/providers" },
            { text: "Agents", link: "/guide/agents" },
            { text: "Routing", link: "/guide/routing" },
          ],
        },
        {
          text: "Operate AgentRouter",
          items: [
            { text: "CLI guide", link: "/guide/cli" },
            { text: "Observability", link: "/guide/observability" },
            { text: "Web UI", link: "/guide/ui" },
            { text: "Troubleshooting", link: "/guide/troubleshooting" },
          ],
        },
      ],
      "/reference/": [
        {
          text: "Reference",
          items: [
            { text: "CLI", link: "/reference/cli" },
            { text: "Configuration", link: "/reference/configuration" },
            { text: "Agent profiles", link: "/reference/agent-profiles" },
          ],
        },
      ],
    },
    outline: "deep",
    search: { provider: "local" },
    socialLinks: [{ icon: "github", link: "https://github.com/mehdihadeli/agent-router" }],
    footer: {
      message: "Route coding agents to the model that fits the task.",
      copyright: "AgentRouter",
    },
  },
});