import { useMDXComponents as getNextraComponents } from "nextra/mdx-components";

const defaultComponents = getNextraComponents();

export function useMDXComponents(components: Record<string, unknown> = {}) {
  return {
    ...defaultComponents,
    ...components,
  };
}