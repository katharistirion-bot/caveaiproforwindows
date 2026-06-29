from pathlib import Path
p = Path(r"D:/caveaiproforwindows/src/CaveAiProForWindows/build/Obfuscation.targets")
text = p.read_text(encoding="utf-8")
override = """
  <!-- Override MSBuild.Obfuscar: generate Obfuscar.xml from template every Release build (never use a committed copy). -->
  <Target Name="Obfuscator"
          AfterTargets="AfterBuild"
          DependsOnTargets="CheckPrerequisites;_GenerateObfuscarConfiguration"
          Condition="'$(Configuration)' == 'Release' and '$(ObfuscatorEnabled)' == 'true'">
    <MakeDir Directories="$(TargetDir)obfuscated" />
    <Exec WorkingDirectory="$(ProjectDir)" Command="$(ObfuscateCommand)" />
    <ItemGroup>
      <FilesToMove Include="$(TargetDir)obfuscated\*.*" />
    </ItemGroup>
    <Move SourceFiles="@(FilesToMove)" DestinationFolder="$(TargetDir)" OverwriteReadOnlyFiles="true" Condition=" '$(OS)' == 'Windows_NT'" />
    <RemoveDir Directories="$(TargetDir)obfuscated" Condition=" '$(OS)' == 'Windows_NT'" />
  </Target>
"""
if "Override MSBuild.Obfuscar" not in text:
    text = text.rstrip()
    if text.endswith("</Project>"):
        text = text[:-len("</Project>")].rstrip() + override + "\n</Project>\n"
        p.write_text(text, encoding="utf-8")
print("patched")
