using DevToys.Api;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

namespace DevToys.MC_NBFXViewer;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(DevToysPluginsAssemblyIdentifier))]
internal sealed class DevToysPluginsAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
    {
        throw new NotImplementedException();
    }
}
