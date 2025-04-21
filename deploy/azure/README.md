# Imageflow Server Azure Deployment Guide

Welcome to the Imageflow Server Azure deployment guide. This document will help you deploy, update, configure, and delete the Imageflow microservice on Azure.

## Prerequisites

- **Azure Subscription**: You need an active Azure subscription. If you don't have one, you can create a free account [here](https://azure.microsoft.com/free/).
- **Permissions**: Ensure you have sufficient permissions to create resources in your Azure subscription.

## Deployment Options

### Deploy via Azure Portal

Click the button below to deploy Imageflow Server to your Azure subscription:

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fimazen%2Fimageflow-server%2Fmain%2Fdeploy%2Fazure%2Fmain.json)



#### Steps:

1. **Click the "Deploy to Azure" Button**: This will open the Azure Portal with the custom deployment blade.
2. **Fill in the Parameters**:
   - **Location**: Choose the Azure region.
   - **Docker Image Tag**: Specify the version of Imageflow Server to deploy (e.g., `v1.0.0`).
   - **Function App Package URL**: Provide the URL to the Azure Function App package.
   - **Cache Retention Days**: Set the number of days before deleting unused cached files.
   - **Custom Domain Name** (optional): Enter a custom domain if you have one.
   - **Enable CDN**: Choose whether to enable CDN in front of the App Service.
   - **Application Settings**: Add any required application settings.
   - **Route Mappings**: Provide route mappings in TOML format.
   - **Secrets**: Enter secrets to be stored securely in Key Vault.
3. **Review and Create**: Validate the deployment summary and click **Create** to start the deployment.
4. **Monitor Deployment**: You can monitor the progress in the Azure Portal.

### Deploy via Azure CLI

You can also deploy using the Azure CLI:

```cmd
az deployment group create ^
  --resource-group <your-resource-group> ^
  --template-file main.json ^
  --parameters @parameters.json
```

Replace <your-resource-group> with your resource group name.

## Updating the Microservice

### Updating the Container Image (Zero-Downtime)

1. **Set Up a Staging Slot** (if not already set up):
   - Navigate to your App Service in the Azure Portal.
   - Click on **Deployment slots** and add a new slot named `staging`.

2. **Deploy New Image to Staging**:
   - Update the container image tag in the staging slot configuration.
     - Go to **Deployment slots** > **staging** > **Configuration** > **General settings**.
     - Under **Container settings**, update **Image and Tag** to the new version.
     - Save and restart the slot.

3. **Test the New Version**:
   - Access the staging slot URL to verify the new version is working as expected.
     - The staging slot URL is usually in the format: `https://your-app-service-name-staging.azurewebsites.net`.

4. **Swap Slots**:
   - In **Deployment slots**, click **Swap**.
     - Set **Source** to `staging` and **Target** to `production`.
     - Click **Swap** to initiate the swap operation.

5. **Monitor and Rollback if Necessary**:
   - Monitor the application in production to ensure it's running smoothly.
   - If issues arise, you can swap back to the previous version by performing another swap between `production` and `staging`.

### Updating the Azure Function

1. **Build and Package the New Version**:
   - Build your Azure Function project using the following command in your project directory:
     ```cmd
     dotnet publish -c Release -o function\publish --framework net8.0
     ```
   - Navigate to the `function\publish` directory.

2. **Create a Zip Package**:
   - Compress the contents of the `publish` folder into a zip file.
     - On Windows, you can use:
       - Right-click the `publish` folder > **Send to** > **Compressed (zipped) folder**.
     - Rename the zip file to include the version number, e.g., `FunctionApp-v1.0.0.zip`.

3. **Upload the Package**:
   - Upload the zip file to a location accessible via a URL.
     - Options include:
       - Attaching it to a GitHub release.
       - Uploading it to Azure Blob Storage with public read access.
       - Hosting it on a web server.

4. **Update Function App Configuration**:
   - Navigate to your Function App in the Azure Portal.
   - Go to **Configuration** > **Application settings**.
   - Find the `WEBSITE_RUN_FROM_PACKAGE` setting and update its value to the new package URL.
     - Example: `https://your-storage-account.blob.core.windows.net/packages/FunctionApp-v1.0.0.zip`.
   - Save the configuration changes.

5. **Restart the Function App**:
   - Navigate to **Overview**.
   - Click on **Restart** to apply the changes.

6. **Verify the Update**:
   - Test the Function App to ensure the new version is running correctly.
     - Use the **Test** feature in the Azure Portal or invoke the function via its endpoint.

7. **Rollback if Necessary**:
   - If issues occur, revert the `WEBSITE_RUN_FROM_PACKAGE` setting to the previous package URL.
   - Save changes and restart the Function App.

---

**Note**: Always ensure that you have backups or previous versions readily available before performing updates. Testing in a non-production environment or using deployment slots can help prevent downtime and mitigate risks.
