# Confluent Apache Kafka for Developers Course

This is the source code accompanying the **Confluent Apache Kafka for Developers** course.

# Exercise 1

## Setup
Starting the correct environment for this exercise

```bash
#We only need three Docker containers for this exercise
$ docker-compose up -d zookeeper kafka tools

#Then we need to use the tools container to open a bash command line
$ docker-compose exec tools bash
```
The tools container has the kafka CLI installed which we can use to connect to any kafka cluster. We will use the tools container bash prompt for the rest of this exercise.

## Create a new topic
```bash
# use the kafka-topics command to create a small topic called testing
root@tools:/ kafka-topics --bootstrap-server kafka:9092 --create --partitions 1 --replication-factor 1 --topic testing
```

## Send messages to topic
```bash
# use the command kafka-console-producer to start typing message and send them to the testing topic
root@tools:/ kafka-console-producer --bootstrap-server kafka:9092 --topic testing
```
This gives you a ```>``` prompt where you can type some text.  Press `ENTER` at the end of each line to send it to Kafka. Press `CTRL + C` to finish sending messages to Kafka


## Read messages from topic
```bash
root@tools:/ kafka-console-consumer --bootstrap-server kafka:9092 --from-beginning --topic testing
```
Wait a short moment and the messages you previously sent into kafka should reappear in the terminal.

## Finishing of
```bash
#Leave the tools container
root@tools:/ exit

#turn of the docker containers to save on resources
$ docker-compose down -v zookeeper kafka tools
```