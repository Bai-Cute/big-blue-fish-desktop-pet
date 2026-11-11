#include <signal.h>
#include <unistd.h>
#include <stdio.h>
#ifdef __APPLE__
#include <sys/event.h>
#include <sys/wait.h>
#else
#include <sys/prctl.h>
#endif
int main(int argc, char **argv) {
    if (argc < 2) return 64;
    pid_t parent = getppid();
#ifdef __APPLE__
    /* macOS has no PDEATHSIG: watch the pet with kqueue and kill the runner when it exits. */
    int queue = kqueue();
    struct kevent watch[4];
    EV_SET(&watch[0], parent, EVFILT_PROC, EV_ADD, NOTE_EXIT, 0, NULL);
    if (queue < 0 || kevent(queue, watch, 1, NULL, 0, NULL) != 0 || getppid() != parent) return 70;
    signal(SIGTERM, SIG_IGN); signal(SIGINT, SIG_IGN); signal(SIGHUP, SIG_IGN);
    pid_t child = fork();
    if (child < 0) return 70;
    if (child == 0) {
        signal(SIGTERM, SIG_DFL); signal(SIGINT, SIG_DFL); signal(SIGHUP, SIG_DFL);
        execvp(argv[1], argv + 1);
        perror("BigBlueFish runner");
        _exit(71);
    }
    EV_SET(&watch[0], child, EVFILT_PROC, EV_ADD, NOTE_EXIT, 0, NULL);
    EV_SET(&watch[1], SIGTERM, EVFILT_SIGNAL, EV_ADD, 0, 0, NULL);
    EV_SET(&watch[2], SIGINT, EVFILT_SIGNAL, EV_ADD, 0, 0, NULL);
    EV_SET(&watch[3], SIGHUP, EVFILT_SIGNAL, EV_ADD, 0, 0, NULL);
    kevent(queue, watch, 4, NULL, 0, NULL); /* a child that already exited is still reaped below */
    for (;;) {
        int status;
        if (waitpid(child, &status, WNOHANG) == child) return WIFEXITED(status) ? WEXITSTATUS(status) : 128 + WTERMSIG(status);
        struct kevent event;
        if (kevent(queue, NULL, 0, &event, 1, NULL) < 1) continue;
        if (event.filter == EVFILT_SIGNAL) kill(child, (int)event.ident);
        else if ((pid_t)event.ident == parent) { kill(child, SIGKILL); waitpid(child, &status, 0); return 70; }
    }
#else
    if (prctl(PR_SET_PDEATHSIG, SIGKILL) != 0 || getppid() != parent) return 70;
    execvp(argv[1], argv + 1);
    perror("BigBlueFish runner");
    return 71;
#endif
}
