# Test Setup and Pipeline Integration

### Links:
- PR Pipeline: [Development - Unit Test Validation (HTGPlus)](https://castandcrew.visualstudio.com/HTG/_build?definitionId=1461)
- Development Build Pipeline: [Development - App Build (All Components)](https://castandcrew.visualstudio.com/HTG/_build?definitionId=812)

## Overview

This directory contains all the unit tests for our project. To ensure quality and 
functionality, we have integrated automated tests into our **Azure DevOps pipeline**.
These tests are executed automatically whenever a **Pull Request (PR)** is created 
targeting the `development` branch, as well as during the build process of the 
`development` branch.

### Pipeline Workflow

1. **PR Validation**: Every time a PR is opened towards the `development` branch, 
the tests will be automatically triggered and validated as part of the **PR pipeline**.
2. **Build Pipeline**: The tests are executed as part of the **build pipeline** 
before the Docker image is built.
3. **Per Project Test Execution**: Each test project is executed individually in 
the pipeline as part of the build process.

## Adding New Tests

To add a new test, follow the steps below. You'll need to update both **pipelines** 
in **Azure DevOps UI** to include the new test project.

### 1. Add the Test Project to the Solution

First, ensure the new test project is added to the solution:
- Create the new test project (e.g., `MyProject.Tests`) in `/Tests` folder.
- Add the project to the solution (if it's not already included).

### 2. Update the Pipeline in Azure DevOps (UI)

For each test project, you'll need to manually add a task in both the **PR pipeline** 
and the **build pipeline** in Azure DevOps.

#### Adding the Test Project to the PR Pipeline:

1. Open your **Azure DevOps project** and navigate to **Pipelines**.
2. Select the [**Development - Unit Test Validation (HTGPlus)**](https://castandcrew.visualstudio.com/HTG/_build?definitionId=1461) and click **Edit**.
3. Add a new **task** to run the unit tests for the newly added test project:

   - **Task**: `.Net Core`
   - **Display Name**: `Tests MyProject`
   - **Command**: `test`
   - **Projects**: `<path_to_your_test_project>/MyProject.Tests.csproj`
   - **Arguments**: `--configuration Release`
   - **Check the option: `Publish test result and code coverage`**

   This step ensures that the tests will be executed when a PR is created.

4. Save the pipeline after adding the task.

#### Adding the Test Project to the Build Pipeline:

1. Open your **Azure DevOps project** and navigate to **Pipelines**.
2. Select the **Build pipeline** for the `development` branch and click **Edit**.
3. Add a new **task** to run the unit tests for the newly added test project:

   - **Task**: `.Net Core`
   - **Display Name**: `Tests MyProject`
   - **Command**: `test`
   - **Projects**: `<path_to_your_test_project>/MyProject.Tests.csproj`
   - **Arguments**: `--configuration Release`
   - **Check the option: `Publish test result and code coverage`**

4. Save the pipeline after adding the task.

### 3. Run the Tests Locally

Before pushing the changes, it's recommended to run the tests locally to ensure everything works as expected:

1. Open a terminal or command prompt.
2. Navigate to the directory where your test project is located.
3. Run the following command to execute the tests:

   ```bash
   dotnet test <path_to_your_test_project>/MyProject.Tests.csproj --configuration Release
   ```
This will run the tests on your machine.

## Viewing Test Results
After the pipeline finishes executing:

The test results will be available in Azure DevOps in the Build summary or PR checks section.
You can view detailed logs and download the test results from the Test Results tab.

## Troubleshooting
If you encounter any issues while executing the tests:

   - Check the test logs for detailed error messages.

   - Ensure that the correct paths are specified for the new test project in the pipelines.

   - Make sure all dependencies are restored (dotnet restore).

## Best Practices for Writing Tests
   - Unit Tests should be fast and isolated. Ensure each test is independent and does not rely on external systems.

   - Use mocking frameworks to isolate dependencies (e.g., Moq).

   - Group tests logically by functionality.

   - Ensure adequate test coverage, especially for critical parts of the application.

# When updating the name of the pipeline (Warning)
After modifying the pipeline name:
   - Send an email to the DevOps tickets mailbox (address in the htg-server README)
to create a new ticket and ask to update the `development` protection rules on GitHub settings
   - Make sure they remove the old reference of the pipeline, and the new reference (new pipeline name) is added
   - Test the changes creating a new Pull Request and seeing if the checks are correct and they are not blocking anything