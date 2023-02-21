#! /bin/zsh

docker buildx build -f arm.Dockerfile -t docker-images.oripoto.pw/dns-sync:arm --push .
docker buildx build -f x86.Dockerfile -t docker-images.oripoto.pw/dns-sync:x86 --push .