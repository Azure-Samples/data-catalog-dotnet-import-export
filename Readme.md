---
services: data-catalog
platforms: dotnet
author: dvana
---

# Data Catalog Import/Export sample tool

The Import/Export tool is a sample that shows how to use the Azure Data Catalog REST API to fetch information from the Azure Data Catalog and how to register items with the catalog. It also shows how to manage a catalog.

To get started using the sample, you first need to build the executable.  You need to do the following steps.

- Download the sample project.
- Open it in Visual Studio and fetch the dependent nuget packages.
- Create (or use an existing) Azure Active Directory Application Id and the corresponding information. If you do not have one and need instructions on how to get one [see here](https://msdn.microsoft.com/en-us/library/azure/mt403303.aspx).
- Add the following to the app config for the app where the first two values are the ones you got from the previous step.
```
     <userSettings>
        <ADCImportExport.Properties.Settings>
            <setting name="ClientId" serializeAs="String">
                <value />
            </setting>
            <setting name="RedirectURI" serializeAs="String">
                <value />
            </setting>
            <setting name="ResourceId" serializeAs="String">
                <value>https://datacatalog.azure.com</value>
            </setting>
        </ADCImportExport.Properties.Settings>
    </userSettings>
```
- Compile the app
- You are now ready to run it.

The app can be run in one of two modes:

**Export**

In export mode you specify -export on the command line and state where you want the exported catalog metadata to go.  Here is an example of the command line:

ADCImportExport.exe -export c:\temp\DemoCatalog.json

The sample tool prompts for credentials (it uses the credentials to identify which catalog it should be exporting from).  Once you have authenticated, it pulls down all the assets from the catalog and stores it in the file location specified.  The file is a json format.  You can inspect the format to get a feel for how it is stored.  The set of information is slightly different than what you get from a search result as the format is setup to be able to easily import the catalog metadata back into the same catalog or a new catalog.

There is also an optional parameter to scope the set of assets you want to export.  Use the -search parameter to specify a search query.  Only the results of the search query will be exported.  Here is an example of the command line:

ADCImportExport.exe -export c:\temp\DemoCatalog.json -search tags=Demo

This will export only the assets that have a tag with the value of Demo. (Note that equality searching is case sensitive so it won’t match assets with a tag equal to “demo” or “dEmo”.  

**Import**

In Import mode you specify -import on the command line and state where the imported catalog exists.  Here is an example of the command line:


ADCImportExport.exe -import c:\temp\DemoCatalog.json

The sample tool prompts for credentials (it uses the credentials to identify which catalog it should be exporting from).  Once you have authenticated, its starts pushing up all the assets found in the json file.

**Caveats**

The sample tool leverages the standard Data Catalog API for searching and registering items in the catalog.  This means that all items are created in the context of the person running the tool. So all notions of ownership both at the asset level (the owner) and the annotation level (the contributor) are lost when doing the export. Because there is no owner, the visibility restrictions are also lost.  When importing a catalog to another tenant this is necessary because none of the identities would exist in the new tenant anyway. Even if you are staying in the same tenant (say exporting, deleting the catalog in one region, recreating it in another region, and then importing) identities cannot be preserved as the annotation owner (i.e. the contributor) is set by the system to the user identity that is logged in when the API is called.  
To simplify the behavior, all items are created with the contributor set to “Everyone” which is a special identity that means any user has permissions to edit or delete that item. (Whereas only the contributor on the original annotation in the original catalog had the power to edit or delete).  The sample tool also changes the creatorId to make this more explicit by putting the word imported_ on the front of the value so it is clear this is an imported item. Users can then copy over the imported metadata into their own new annotations for which they are the contributor.
