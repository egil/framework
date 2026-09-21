#!/usr/bin/env python3
"""Exercise packed NuGets in isolated projects and separate writer/reader processes."""
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

feed = pathlib.Path(sys.argv[1]).resolve()
versions = {}
for package in feed.glob('*.nupkg'):
    with zipfile.ZipFile(package) as archive:
        nuspec = ET.fromstring(archive.read(next(n for n in archive.namelist() if n.endswith('.nuspec'))))
        ns = {'n': 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
        metadata = nuspec.find('n:metadata', ns)
        versions[metadata.find('n:id', ns).text] = metadata.find('n:version', ns).text

with tempfile.TemporaryDirectory(prefix='om-package-consumer-') as temporary:
    root = pathlib.Path(temporary)
    (root / 'Directory.Build.props').write_text('<Project />')
    (root / 'Directory.Packages.props').write_text('<Project />')
    (root / 'global.json').write_text('{}')
    def run(*args, cwd=root):
        subprocess.run(args, cwd=cwd, check=True, timeout=180)
    core = root / 'Core'
    core.mkdir()
    (core / 'Core.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Egil.Orleans.Messaging" Version="{versions["Egil.Orleans.Messaging"]}" /></ItemGroup></Project>')
    run('dotnet', 'restore', str(core / 'Core.csproj'), '--source', str(feed), '--source', 'https://api.nuget.org/v3/index.json')
    assets = json.loads((core / 'obj/project.assets.json').read_text())
    assert not any(name.startswith('Microsoft.Orleans.Journaling/') for name in assets['libraries']), 'Core depends on preview journaling'
    consumer = root / 'Consumer'
    shutil.copytree(pathlib.Path(__file__).resolve().parents[1] / 'test/PackageConsumer', consumer, ignore=shutil.ignore_patterns('bin', 'obj'))
    version = versions['Egil.Orleans.Messaging.Journaling']
    assert '-preview' in version, 'Companion must remain prerelease'
    run('dotnet', 'restore', str(consumer / 'PackageConsumer.csproj'), f'-p:JournalingVersion={version}', '--source', str(feed), '--source', 'https://api.nuget.org/v3/index.json')
    run('dotnet', 'build', str(consumer / 'PackageConsumer.csproj'), '-c', 'Release', '--no-restore', f'-p:JournalingVersion={version}')
    dll = consumer / 'bin/Release/net10.0/PackageConsumer.dll'
    run('dotnet', str(dll), 'write', str(root / 'journal'))
    assert any('custom:durable' in f.read_text() for f in (root / 'journal').glob('*.journal')), 'Host converter not used'
    run('dotnet', str(dll), 'read', str(root / 'journal'))
print('Core dependency isolation and companion process restart passed.')
