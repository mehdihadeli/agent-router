<script setup lang="ts">
import { withBase } from "vitepress";
</script>

<template>
  <div>
    <section class="home-route-panel">
      <div class="route-panel-copy">
        <p class="eyebrow">A calm control plane for model work</p>
        <h2>Your agent loop stays yours.</h2>
        <p>AgentRouter handles provider identity, model selection, translation, retries, and local visibility. Your agent still owns tools, files, permissions, and the work.</p>
        <div class="route-stats">
          <span><strong>01</strong> gateway</span>
          <span><strong>n</strong> providers</span>
          <span><strong>0</strong> global setting edits</span>
        </div>
      </div>
      <div class="route-diagram" aria-label="AgentRouter request flow">
        <div class="route-node route-node-agent"><span>AGENT</span><strong>Claude Code</strong><small>or another client</small></div>
        <div class="route-line"><i></i><span>local request</span></div>
        <div class="route-node route-node-router"><span>AR</span><strong>AgentRouter</strong><small>route + translate</small></div>
        <div class="route-line"><i></i><span>selected model</span></div>
        <div class="route-node route-node-provider"><span>UPSTREAM</span><strong>Provider</strong><small>cloud or local</small></div>
      </div>
    </section>

    <section class="home-start-grid">
      <div>
        <p class="eyebrow">First five minutes</p>
        <h2>From zero to routed.</h2>
        <p>Install the tool, register one model, choose a route, and launch an isolated profile.</p>
      </div>
      <div class="home-terminal">
        <div class="terminal-bar"><span></span><span></span><span></span><small>agent-router</small></div>
        <pre><code><span class="terminal-muted"># install</span>
dotnet tool install --global AgentRouter

<span class="terminal-muted"># configure</span>
ar init --port 4600
ar provider add work --base-url https://api.openai.com/v1 --models gpt-4.1
ar route set work/gpt-4.1

<span class="terminal-muted"># run</span>
ar serve</code></pre>
      </div>
    </section>

    <section class="home-docs-band">
      <p class="eyebrow">Documentation map</p>
      <h2>Choose your next move.</h2>
      <div class="home-doc-links">
        <a :href="withBase('/guide/quick-start')"><strong>01 / Quick start</strong><span>Install, configure, and make your first request.</span></a>
        <a :href="withBase('/guide/providers')"><strong>02 / Providers</strong><span>Connect a native or compatible upstream.</span></a>
        <a :href="withBase('/guide/routing')"><strong>03 / Routing</strong><span>Shape defaults, tiers, rules, and fallback.</span></a>
        <a :href="withBase('/reference/configuration')"><strong>04 / Configuration</strong><span>Understand saved settings and environment paths.</span></a>
      </div>
    </section>
  </div>
</template>
