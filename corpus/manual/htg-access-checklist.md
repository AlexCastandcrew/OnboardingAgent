# HTG access and tools checklist

To work on HTG, a new team member needs the applications, accesses and tools below. **Each access is requested through a
ticket.** The list is the standard set for HTG; request all of it when you join.

The source list has two more columns, "Access to be provided by" and "Status", that were empty. This document does not say
who grants each access.

## Accesses to request

| # | Resource | Detail |
|---|---|---|
| 1 | Jira dashboard and Confluence | HTG board in Jira: https://castandcrew.atlassian.net/jira/software/c/projects/HTG/boards/91 |
| 2 | HTG git access | Repository: https://github.com/cast-and-crew/htg-server |
| 3 | 1Password | The HTG dev vault and the shared AKS vault |
| 4 | npmjs.org | Access to the npm registry (and the npm organization invitation, see the tools below) |
| 5 | Azure DevOps | HTG project builds: https://castandcrew.visualstudio.com/HTG/_build |
| 6 | Snyk | Add the user to Snyk |
| 7 | Active Directory groups | Add the user to the group `HTGPLus Admins-Dev`. Remember to sync Okta afterwards. |
| 8 | HTG Portal | QA: https://qa.htgplus.castandcrew.com/#/ and Dev: https://dev.htgplus.castandcrew.com/#/ . Access is needed in both the US and the Canada region. |
| 9 | Claude | Access to Claude |
| 10 | GitHub Copilot | Access to GitHub Copilot |

## Tools to install

11. Required tools:
    - VS Code
    - NPM organization invitation (accept it after the request)
    - Postman
    - Visual Studio (latest)
    - Docker Desktop
    - SSMS (the source list writes "MSSM")
    - Node.js
    - .NET 9

## Notes

- Request the accesses first: the tools and the local setup depend on the repository, npm, 1Password and the Active
  Directory group.
- After the Active Directory group is added, sync Okta so the access takes effect.
- The HTG Portal has separate QA and Dev addresses, and each exists for the US and the Canada region.
