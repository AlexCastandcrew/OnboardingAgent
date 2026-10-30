import { describeSource } from './source-info';

describe('describeSource', () => {
  it('describeSource_GitHubFileUrl_ReturnsRepositoryAndPath', () => {
    const info = describeSource({ sourceType: 'repo', url: 'https://github.com/acme/htg-server/blob/master/docs/Some%20File.md#intro' });

    expect(info).toEqual({ platform: 'acme/htg-server', path: 'docs/Some File.md', link: 'https://github.com/acme/htg-server/blob/master/docs/Some%20File.md#intro' });
  });

  it('describeSource_ConfluenceAndJiraUrls_ReturnSpaceAndProjectKeys', () => {
    expect(describeSource({ sourceType: 'confluence', url: 'https://x.atlassian.net/wiki/spaces/H2G/pages/1/Title' }).platform).toBe('H2G');
    expect(describeSource({ sourceType: 'jira', url: 'https://x.atlassian.net/browse/H2G-7165' }).platform).toBe('H2G');
  });

  it('describeSource_NoUrlOrNonWebUrl_OffersNoLink', () => {
    expect(describeSource({ sourceType: 'manual', url: null })).toEqual({ platform: null, path: null, link: null });
    expect(describeSource({ sourceType: 'repo', url: 'javascript:alert(1)' }).link).toBeNull();
  });
});
