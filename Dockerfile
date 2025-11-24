FROM icyavocado/stardew-multiplayer:latest

RUN sudo apt update && \
  sudo apt install -y wget apt-transport-https && \
  wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb && \
  sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb && \
  sudo apt update && sudo apt install -y dotnet-sdk-10.0 && \
  sudo apt install -y binutils neovim

USER app

ADD ./stardewvalley.targets /home/app/stardewvalley.targets

WORKDIR /home/app
