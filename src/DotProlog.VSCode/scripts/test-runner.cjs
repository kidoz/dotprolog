const path = require("node:path");
const Mocha = require("mocha");

exports.run = async () => {
    const mocha = new Mocha({ ui: "tdd", timeout: 45000 });
    mocha.addFile(
        path.join(
            __dirname,
            `../bin/integration/${process.env.DOTPROLOG_TEST_RESTRICTED ? "restricted" : "editor"}.test.js`,
        ),
    );
    await new Promise((resolve, reject) =>
        mocha.run((failed) =>
            failed
                ? reject(new Error(`${failed} editor tests failed`))
                : resolve(),
        ),
    );
};
