# Accessibility

AgentConnector aims to make its documentation, contribution process, and sample interfaces usable by people with disabilities. Accessibility improvements and reports of barriers are welcome.

## Scope and environments

AgentConnector is primarily an embeddable .NET / ASP.NET Core library and HTTP API. Most interaction happens through a host application, development tools, or API clients. Host applications are responsible for the accessibility of their own interfaces.

The repository contains Markdown documentation and a browser-based Atlassian sign-in sample. Development requirements are defined in [global.json](global.json) and [Directory.Build.props](Directory.Build.props); CI builds and tests run on Windows and Linux. Those build results do not establish browser or assistive-technology compatibility.

For documentation and sample UI changes, aim for clear language, descriptive links, meaningful headings, labelled controls, keyboard access, visible focus, readable contrast, and status messages that assistive technology can perceive. Do not rely on color alone to communicate meaning.

## Known limitations

- The project has not completed a formal accessibility audit or established a tested browser, screen reader, and assistive-technology support matrix. No WCAG conformance claim is made.
- The Atlassian sample opens an OAuth popup and depends on third-party sign-in and consent pages. Popup blocking, focus changes, and external pages can affect usability. If the popup cannot notify the parent page, return to the sample and select **Check connections**. See the [sample guide](samples/AgentConnector.Server/ATLASSIAN.md).
- The sample is a development demonstration; host applications should evaluate their complete sign-in and connection-management experience before deployment.

## Reporting a barrier

Open an [accessibility report](https://github.com/jihadkhawaja/agentconnector/issues/new?template=accessibility.yml). Describe the affected page or workflow, what you expected, what happened, and any workaround. Include your operating system, browser or client, and assistive technology and versions when relevant and comfortable to share. You do not need to disclose a diagnosis or other personal medical information.

Use synthetic values and remove credentials, personal information, and OAuth callback parameters from screenshots and logs. Suspected vulnerabilities belong in the private process described in [SECURITY.md](SECURITY.md).

Maintainers will use reports to reproduce barriers and prioritize improvements. Contributions can follow [CONTRIBUTING.md](CONTRIBUTING.md); please explain how an accessibility change was checked and any environments that remain untested.
