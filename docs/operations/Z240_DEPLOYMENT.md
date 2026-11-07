# Z240 deployment (API as a Scheduled Task)

The API runs as the Scheduled Task `CreatorFlow-Api`, not as a child of the GitHub runner, so it keeps
running after a workflow job ends. Each deployment publishes to `E:\CreatorFlowDeploy\releases\<time>-<sha>`,
repoints the junction `E:\CreatorFlowDeploy\current`, restarts the task and checks `/health`. If the new
release is unhealthy the previous release is restored and the workflow fails.

## One-time setup (elevated PowerShell on the Z240)
1. Decide the Windows account that runs the API. Use the account that previously ran it, so DPAPI-protected
   Data Protection keys still decrypt tenant connector credentials.
2. Add `CREATORFLOW_DATA_PROTECTION_KEYS_PATH=<fixed folder>` to `E:\Projects\CreatorFlow-AI\.env` (readable only by that
   account) before the first deployment.
3. Run `scripts\deploy\Register-CreatorFlowApiTask.ps1 -UserName <account>`.
4. The runner account must be able to start/stop the task (administrator is sufficient).
5. First deployment from a manually started API: run
   `scripts\deploy\Deploy-CreatorFlowApi.ps1 -AdoptLegacyProcess` once from a normal PowerShell prompt and watch it.
6. In GitHub: Settings -> Environments -> `z240-dev` -> add required reviewers.


## One-time SQL QC runner permission (elevated PowerShell)

The self-hosted runner service currently runs as `NT AUTHORITY\NETWORK SERVICE`. It must not be
a SQL Server sysadmin. From an elevated PowerShell session using a SQL sysadmin identity, run:

```powershell
.\database\Grant-SqlQcRunnerAccess.ps1 -SqlInstance localhost -Database CreatorFlow_QC
```

The script first verifies the database tags (`CreatorFlowEnvironment=QC` and
`CreatorFlowRepository=CreatorFlow-AI`), then grants only `db_ddladmin`,
`db_datareader`, `db_datawriter`, and `ALTER` on the `dbo` schema to the runner
identity in `CreatorFlow_QC`. It does not grant server-level permissions or access to other
databases. Re-run the SQL Server self-hosted QC workflow after this one-time setup.

## Operations
- Deploy: Actions -> "Deploy to Z240" -> Run workflow (approval required).
- Logs: `E:\CreatorFlowDeploy\logs\api-<date>.log`.
- Manual rollback: stop the task, repoint `current` to an earlier folder in `releases`, start the task.
- Not covered: database migrations are not applied by this workflow; run the SQL QC/migration process first.