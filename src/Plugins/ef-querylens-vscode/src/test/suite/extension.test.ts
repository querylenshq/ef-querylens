import * as assert from "assert";
import * as vscode from "vscode";

suite("EF QueryLens Extension", () => {
  test("Extension is present", async () => {
    const extension = vscode.extensions.getExtension("EFQueryLens.ef-querylens-vscode");
    assert.ok(extension, "Extension should be discoverable by id");
  });

  test("Extension activates when runtime is packaged", async () => {
    const extension = vscode.extensions.getExtension("EFQueryLens.ef-querylens-vscode");
    assert.ok(extension, "Extension should be discoverable by id");

    await extension!.activate();
    assert.strictEqual(extension!.isActive, true, "Extension should activate successfully");
  });

  test("Registers QueryLens commands", async () => {
    const extension = vscode.extensions.getExtension("EFQueryLens.ef-querylens-vscode");
    assert.ok(extension, "Extension should be discoverable by id");

    await extension!.activate();

    const commands = await vscode.commands.getCommands(true);
    assert.ok(
      commands.some((command) => command.startsWith("efquerylens.")),
      "Extension should register efquerylens.* commands",
    );
  });
});
