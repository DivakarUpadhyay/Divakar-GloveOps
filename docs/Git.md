# Git — version control

## What is Git?
**Git** is a free, open-source **distributed version control system**, created by Linus Torvalds in 2005. It records every change to your files as **commits**, so you can see the full history, compare versions, undo mistakes and let many developers work on the same code at once. "Git" is a name, not an abbreviation.

Main concepts:
- **Repository (repo):** the project folder plus its full history.
- **Commit:** a saved snapshot with a message ("Add QA approval step").
- **Branch:** a separate line of work (`feature/delegation`) that doesn't disturb `main`.
- **Merge / rebase:** combine branches. Merge keeps both histories; rebase replays your commits on top.
- **Remote:** a shared copy on a server: **GitHub**, GitLab, Azure DevOps or Bitbucket.
- **Pull request (PR):** a request to merge a branch, where teammates review the code first.
- **Tag:** a named release point (`v1.0.0`).
- **.gitignore:** files Git must never track (`bin/`, `obj/`, secrets).

Everyday commands:
```bash
git pull                                   # get the latest changes
git switch -c feature/sample-delegation    # new branch
git add . && git commit -m "Add delegation page"
git push -u origin feature/sample-delegation   # then open a Pull Request
git log --oneline --graph                  # see history
git revert <commit>                        # undo safely on a shared branch
git stash                                  # park unfinished work
```

## Why companies use it
- **History and rollback:** every change is recorded with who, when and why. A bad change can be undone.
- **Teamwork:** many developers work in parallel on branches, without overwriting each other.
- **Code review:** pull requests catch bugs and share knowledge before code reaches production.
- **CI/CD (Continuous Integration / Continuous Delivery):** every push can automatically build, test and deploy.
- **Audit and compliance:** a clear record of what changed in each release.

## Typical uses (in an internal IT team)
| Use | How |
|---|---|
| Feature development | `feature/*` branch → PR → review → merge to `main` |
| Hotfix in production | `hotfix/*` branch from the release tag → fix → merge back |
| Releases | tag `v1.2.0`, write a CHANGELOG |
| Non-code assets | version n8n workflow JSON, SQL scripts, Power BI files, documentation |
| Automated checks | GitHub Actions / Azure Pipelines build and test on every PR |

## Good practice
- Small commits with clear messages. Pull often to avoid big conflicts.
- Rebase only your **own** local branches. On shared branches use merge or `revert`, never rewrite history.
- **Never commit secrets.** Use user secrets or environment variables. If a secret is committed: rotate it first, then clean the history.
- Resolve conflicts by understanding both changes, then **re-test**.

## Where it's used
**In GloveOps:**
- Repo: https://github.com/DivakarUpadhyay/Divakar-GloveOps (branch `main`).
- **CI:** `.github/workflows/build.yml` restores, builds and runs the tests on every push and pull request. Database tests skip automatically on GitHub's servers, which have no SQL Server.
- **`.gitignore`** keeps `bin/`, `obj/` and local settings out. API keys live in **user secrets**, not in the repo.
- **Versioned beyond code:** SQL scripts (`database/`), n8n workflows (`n8n/*.json`), DAX measures (`powerbi/measures.dax`), Dify knowledge files and all documentation.

**In real companies:** every software team. Internal IT teams host repos on GitHub, GitLab or Azure DevOps, protect `main` with required reviews, and deploy from tagged releases.
