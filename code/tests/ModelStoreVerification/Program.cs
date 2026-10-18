using BigBlueFish.Deployment;

string root = Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
string lower = Path.Combine(root,"models"), upper = Path.Combine(root,"MODELS");
Directory.CreateDirectory(lower); Directory.CreateDirectory(upper);
string source = Path.Combine(upper,"sample.gguf"), destination = Path.Combine(lower,"sample.gguf");
// A header fixture exercises file import; it is never passed to an inference process.
using (var writer = new BinaryWriter(File.Create(source))) { writer.Write(0x46554747u); writer.Write(3u); writer.Write(1ul); writer.Write(1ul); }
string hash = await ModelStore.Hash(source);
await ModelStore.Import(root,source,hash);
if (!File.Exists(destination) || await ModelStore.Hash(destination) != hash) throw new Exception("case-distinct source was not imported");
if (await ModelStore.Hash(source) != hash) throw new Exception("source file changed");
if (ModelStore.Resolve(root) != destination) throw new Exception("selection does not resolve imported file");
if (File.Exists(destination+".importing")) throw new Exception("temporary import not completed");
Console.WriteLine("Model import file operations: case-distinct paths, byte identity, unchanged source, selected destination and completed import passed. No inference fixture used.");
