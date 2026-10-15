import { generateStaticParamsFor, importPage } from "nextra/pages";

export const generateStaticParams = generateStaticParamsFor("mdxPath");

function getPathSegments(mdxPath?: string | string[]) {
  if (Array.isArray(mdxPath)) return mdxPath;
  return mdxPath ? mdxPath.split("/") : [];
}

const guidePages = [
  ["quick-start", "Quick start"],
  ["providers", "Providers"],
  ["agents", "Agents"],
  ["routing", "Routing"],
  ["cli", "CLI guide"],
  ["observability", "Observability"],
  ["ui", "Web UI"],
  ["troubleshooting", "Troubleshooting"],
];

const referencePages = [
  ["cli", "CLI"],
  ["configuration", "Configuration"],
  ["agent-profiles", "Agent profiles"],
];

function DocsNavigation({ path }: { path: string[] }) {
  const basePath = process.env.GITHUB_ACTIONS && process.env.GITHUB_REPOSITORY
    ? `/${process.env.GITHUB_REPOSITORY.split("/")[1]}`
    : "";
  const currentPath = path.join("/");

  return (
    <aside className="docs-navigation" aria-label="Documentation navigation">
      <div>
        <strong>Guide</strong>
        <nav>
          {guidePages.map(([slug, label]) => (
            <a
              href={`${basePath}/guide/${slug}`}
              className={currentPath === `guide/${slug}` ? "active" : undefined}
              key={slug}
            >
              {label}
            </a>
          ))}
        </nav>
      </div>
      <div>
        <strong>Reference</strong>
        <nav>
          {referencePages.map(([slug, label]) => (
            <a
              href={`${basePath}/reference/${slug}`}
              className={currentPath === `reference/${slug}` ? "active" : undefined}
              key={slug}
            >
              {label}
            </a>
          ))}
        </nav>
      </div>
      <div>
        <strong>More</strong>
        <nav>
          <a
            href={`${basePath}/integrations`}
            className={currentPath === "integrations" ? "active" : undefined}
          >
            Integrations
          </a>
        </nav>
      </div>
    </aside>
  );
}

export async function generateMetadata(props: { params: Promise<{ mdxPath?: string[] }> }) {
  const params = await props.params;
  const { metadata } = await importPage(getPathSegments(params.mdxPath));
  return metadata;
}

export default async function Page(props: { params: Promise<{ mdxPath?: string[] }> }) {
  const params = await props.params;
  const result = await importPage(getPathSegments(params.mdxPath));
  const { default: MDXContent } = result;
  const path = getPathSegments(params.mdxPath);
  const isDocumentationPage = path.length > 0;

  return (
    <main className={`nextra-content${isDocumentationPage ? " docs-page" : ""}`}>
      {isDocumentationPage && <DocsNavigation path={path} />}
      <article className="docs-article">
        <MDXContent {...props} params={params} />
      </article>
    </main>
  );
}