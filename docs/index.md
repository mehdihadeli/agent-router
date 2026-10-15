---
layout: home
title: AgentRouter
titleTemplate: Local model routing for coding agents
description: A local gateway for connecting coding agents to the provider and model that fits the task.
hero:
  name: AgentRouter
  text: Route every agent to the right model.
  tagline: One loopback gateway for providers, profiles, tools, streaming, and deliberate model selection.
  actions:
    - theme: brand
      text: Start here
      link: /guide/quick-start
    - theme: alt
      text: View the CLI
      link: /reference/cli
    - theme: link
      text: GitHub
      link: https://github.com/mehdihadeli/agent-router
features:
  - icon: 01
    title: One local endpoint
    details: Keep agent configuration stable while AgentRouter translates requests to the provider you choose.
    link: /integrations
    linkText: See protocol support
  - icon: 02
    title: Routing with intent
    details: Set defaults, Claude tiers, conditional rules, rewrites, and ordered fallback candidates.
    link: /guide/routing
    linkText: Explore routing
  - icon: 03
    title: Profiles without drift
    details: Save isolated launch profiles so experiments do not rewrite your global agent settings.
    link: /guide/agents
    linkText: Configure profiles
  - icon: 04
    title: Providers stay explicit
    details: Connect OpenAI-compatible, Anthropic, Azure OpenAI, GitHub Models, and compatible endpoints.
    link: /guide/providers
    linkText: Add a provider
  - icon: 05
    title: Tools and streams intact
    details: Forward system instructions, streaming, function tools, tool results, and supported content.
    link: /integrations
    linkText: Check compatibility
  - icon: 06
    title: Local control surface
    details: Use the ar CLI, managed service lifecycle, masked configuration, and opt-in request telemetry.
    link: /guide/cli
    linkText: Learn the workflow
---

<HomeSections />
