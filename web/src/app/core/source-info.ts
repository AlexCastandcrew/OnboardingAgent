import { ChatSource } from './models';

export interface SourceInfo {
  /** Repository (owner/name), Confluence space or Jira project, when the URL says so; null otherwise. */
  platform: string | null;
  /** File path inside the repository, when the URL is a GitHub file link. */
  path: string | null;
  /** The original, only when the source has a web URL: never built from guesses. */
  link: string | null;
}

const GITHUB_FILE = /^https:\/\/github\.com\/([^/\s]+\/[^/\s]+)\/(?:blob|tree)\/[^/\s]+\/(.+?)(?:[?#].*)?$/;
const GITHUB_REPO = /^https:\/\/github\.com\/([^/\s]+\/[^/\s]+?)(?:\/|$)/;
const CONFLUENCE_SPACE = /\/wiki\/spaces\/([^/\s]+)\//;
const JIRA_KEY = /\/browse\/([A-Z][A-Z0-9]+)-\d+/;

/** What the source's own URL tells about where it lives. Only http(s) links are offered as links. */
export function describeSource(source: Pick<ChatSource, 'url' | 'sourceType'>): SourceInfo {
  const url = source.url;
  if (!url || !/^https?:\/\//i.test(url)) {
    return { platform: null, path: null, link: null };
  }

  if (source.sourceType === 'repo') {
    const file = GITHUB_FILE.exec(url);
    if (file) {
      return { platform: file[1], path: decodeURIComponent(file[2]), link: url };
    }

    return { platform: GITHUB_REPO.exec(url)?.[1] ?? null, path: null, link: url };
  }

  if (source.sourceType === 'confluence') {
    return { platform: CONFLUENCE_SPACE.exec(url)?.[1] ?? null, path: null, link: url };
  }

  if (source.sourceType === 'jira') {
    return { platform: JIRA_KEY.exec(url)?.[1] ?? null, path: null, link: url };
  }

  return { platform: null, path: null, link: url };
}
