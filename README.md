# Expired Domain Finder

This is a simple Windows application designed to identify domains at risk of expiration that are used by
maintainers of an open source project.

It currently only supports npm.

The tool uses three methods of looking up domain registration records:

* The NuGet [Whois](https://www.nuget.org/packages/Whois) package
* The Docker [dentch/whois](https://hub.docker.com/r/dentych/whois) image (pulled in automatically if Docker is installed), optional
* A locally installed "whois" executable (provided by you), optional

## Using

To use it, just run the application and type the name of one or more npm projects in the text box, and click "Start".
Windows application to find expired domains attached to open source projects (currently npm only).

## Bugs

If you find any bugs, please open an issue or submit a pull request.

## Security

If you find a security vulnerability, please report it to me privately at michael[-dot-]scovetta[-at-]gmail.com.


